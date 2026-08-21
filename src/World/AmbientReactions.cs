using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LooseLips.Context;
using LooseLips.Core;
using LooseLips.Dialog;
using LooseLips.Player2;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// People saying something about what just happened, instead of standing there.
    ///
    /// The game already decides when somebody has noticed something worth remarking on - that
    /// is what DialogController.SeenOrHeardUnusual is for - and it answers with a canned line.
    /// This listens for the same moment and answers with one written for the person, the thing
    /// they saw, and where they are standing. It also watches for the states the game does not
    /// announce: somebody suddenly terrified, a fight starting, a neighbour bolting.
    ///
    /// The volume is chosen by the model and it matters: a shout brings the street, a whisper
    /// reaches only whoever is being leaned towards. Alarm tends to be loud and gossip tends to
    /// be quiet, which is roughly how it works when people are startled in public.
    ///
    /// Everything here is optional, rationed by <see cref="RequestBudget"/>, and only ever runs
    /// where the player is close enough to hear it - a reaction nobody witnesses is generation
    /// time spent on nothing.
    /// </summary>
    public static class AmbientReactions
    {
        /// <summary>What we noticed, in the words the model will be given.</summary>
        private sealed class Trigger
        {
            public Citizen Who;
            public string What;
            public VoiceLevel Suggested;
        }

        private sealed class Watched
        {
            public float Alertness;
            public bool InCombat;
            public bool Fleeing;
            public bool Bleeding;
            public bool Trespassing;
        }

        /// <summary>
        /// Whether the player is somewhere they should not be. Worth a remark even when nobody
        /// is alarmed yet - being noticed before anything has gone wrong is most of what makes
        /// a place feel occupied.
        /// </summary>
        private static bool PlayerIsTrespassing()
        {
            try
            {
                var player = Player.Instance;
                if (player == null || player.currentRoom == null) return false;
                int escalation;
                return player.IsTrespassing(player.currentRoom, out escalation);
            }
            catch
            {
                return false;
            }
        }

        private static readonly Dictionary<int, Watched> LastSeen = new Dictionary<int, Watched>();

        /// <summary>What the player was last seen holding, so a change can be noticed.</summary>
        private static string _playerHeld = "";
        private static bool _playerArmed;
        private static bool _watchingPlayerStarted;
        private static readonly Queue<Trigger> Pending = new Queue<Trigger>();
        private static float _nextPoll;

        public static string LastLine { get; private set; } = "None yet.";

        /// <summary>
        /// Called from the game's own "somebody noticed something" hook. Cheap and defensive:
        /// this runs inside a Harmony postfix shared with other mods, so it must never throw
        /// and must never block.
        /// </summary>
        public static void NoticedSomething(Citizen who, Actor about, NewRoom where)
        {
            if (!ModConfig.EnableAmbientLife.Value) return;
            if (who == null) return;

            try
            {
                if (!CanBeHeardByPlayer(who)) return;

                var what = "You have just noticed something out of place";
                if (about != null)
                {
                    var name = SafeName(about);
                    if (!string.IsNullOrEmpty(name)) what = "You have just caught " + name + " doing something they should not be";
                }
                if (where != null)
                {
                    try { what += " in " + where.GetName(); } catch { }
                }

                Enqueue(new Trigger { Who = who, What = what + ".", Suggested = VoiceLevel.Normal });
            }
            catch { }
        }

        /// <summary>
        /// Watch for the changes the game does not announce. Polled rather than patched: these
        /// are plain fields, and a poll a couple of times a second costs nothing next to the
        /// generation it might trigger.
        /// </summary>
        public static void Tick()
        {
            if (!ModConfig.EnableAmbientLife.Value) return;

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + 0.5f;
                try { Poll(); } catch { }
                try { WatchThePlayer(); } catch { }
            }

            Drain();
        }

        private static void Poll()
        {
            var player = Player.Instance;
            if (player == null) return;

            foreach (var c in Earshot.CitizensWhoCanHear(player, false))
            {
                if (c == null || c.ai == null) continue;

                Watched last;
                var known = LastSeen.TryGetValue(c.humanID, out last);
                if (!known)
                {
                    LastSeen[c.humanID] = new Watched
                    {
                        Alertness = c.ai.alertness,
                        InCombat = c.ai.inCombat,
                        Fleeing = c.ai.inFleeState
                    };
                    continue;   // first sight is not an event
                }

                var now = c.ai.alertness;

                if (!last.InCombat && c.ai.inCombat)
                {
                    Enqueue(new Trigger
                    {
                        Who = c,
                        What = "A fight has just broken out and you are in it.",
                        Suggested = VoiceLevel.Shout
                    });
                }
                else if (!last.Fleeing && c.ai.inFleeState)
                {
                    Enqueue(new Trigger
                    {
                        Who = c,
                        What = "You have just decided to run.",
                        Suggested = VoiceLevel.Shout
                    });
                }
                else if (!last.Bleeding && c.bleeding > 0.01f)
                {
                    Enqueue(new Trigger
                    {
                        Who = c,
                        What = "You are bleeding, and it has only just registered.",
                        Suggested = VoiceLevel.Shout
                    });
                }
                else if (last.Trespassing != PlayerIsTrespassing() && PlayerIsTrespassing())
                {
                    Enqueue(new Trigger
                    {
                        Who = c,
                        What = "The investigator has just walked into somewhere they have no business being.",
                        Suggested = VoiceLevel.Normal
                    });
                }
                else if (now - last.Alertness >= ModConfig.AlarmJumpToReact.Value)
                {
                    Enqueue(new Trigger
                    {
                        Who = c,
                        What = "Something has just badly frightened you.",
                        Suggested = VoiceLevel.Shout
                    });
                }

                last.Alertness = now;
                last.InCombat = c.ai.inCombat;
                last.Fleeing = c.ai.inFleeState;
                try { last.Bleeding = c.bleeding > 0.01f; } catch { }
                last.Trespassing = PlayerIsTrespassing();
            }
        }

        /// <summary>
        /// Notice what the player themselves is doing.
        ///
        /// Everything else here reacts to the world; this reacts to you, which is the half that
        /// makes a street feel like it is watching. Only changes are reported: standing around
        /// holding a wrench is not news, drawing one is. The first reading is swallowed so
        /// loading a save does not read as you producing a weapon from nowhere.
        /// </summary>
        private static void WatchThePlayer()
        {
            if (!ModConfig.ReactToWhatYouDo.Value) return;

            var player = Player.Instance;
            if (player == null) return;

            string held = "";
            var armed = false;

            try
            {
                var item = player.rightHandInteractable ?? player.leftHandInteractable;
                if (item != null)
                {
                    held = item.GetName();
                    try { armed = item.preset != null && item.preset.weapon != null; }
                    catch { armed = false; }
                }
            }
            catch { return; }

            if (!_watchingPlayerStarted)
            {
                _watchingPlayerStarted = true;
                _playerHeld = held;
                _playerArmed = armed;
                return;
            }

            if (held == _playerHeld && armed == _playerArmed) return;

            var wasArmed = _playerArmed;
            _playerHeld = held;
            _playerArmed = armed;

            // Only somebody who can actually see it has anything to say about it.
            var witness = NearestWatcher();
            if (witness == null) return;

            if (armed && !wasArmed)
            {
                Enqueue(new Trigger
                {
                    Who = witness,
                    What = "The investigator in front of you has just drawn a " + Describe(held) + ".",
                    Suggested = VoiceLevel.Shout
                });
            }
            else if (!armed && wasArmed)
            {
                Enqueue(new Trigger
                {
                    Who = witness,
                    What = "The investigator has just put their weapon away.",
                    Suggested = VoiceLevel.Normal
                });
            }
            else if (!string.IsNullOrEmpty(held))
            {
                Enqueue(new Trigger
                {
                    Who = witness,
                    What = "The investigator has just taken out a " + Describe(held) + ".",
                    Suggested = VoiceLevel.Whisper
                });
            }
        }

        private static string Describe(string item)
            => string.IsNullOrWhiteSpace(item) ? "something" : item.ToLowerInvariant();

        /// <summary>The closest person who could plausibly have seen it.</summary>
        private static Citizen NearestWatcher()
        {
            var player = Player.Instance;
            if (player == null) return null;

            Citizen best = null;
            var bestDistance = float.MaxValue;

            foreach (var c in Earshot.CitizensWhoCanHear(player, false))
            {
                if (c == null || c.ai == null) continue;
                try
                {
                    if (c.isAsleep || c.isDead) continue;
                    var d = Vector3.Distance(c.transform.position, player.transform.position);
                    if (d < bestDistance) { bestDistance = d; best = c; }
                }
                catch { }
            }
            return best;
        }

        private static void Enqueue(Trigger trigger)
        {
            if (trigger?.Who == null) return;
            if (Pending.Count >= 4) return;      // a backlog is stale by the time it is spoken
            Pending.Enqueue(trigger);
        }

        private static void Drain()
        {
            if (Pending.Count == 0) return;

            var trigger = Pending.Peek();
            if (trigger?.Who == null) { Pending.Dequeue(); return; }

            if (!RequestBudget.TryTake(RequestBudget.Kind.Ambient, trigger.Who)) return;

            Pending.Dequeue();
            Generate(trigger);
        }

        private static void Generate(Trigger trigger)
        {
            string prompt;
            try
            {
                prompt = BuildPrompt(trigger);
            }
            catch
            {
                RequestBudget.Finished(RequestBudget.Kind.Ambient);
                return;
            }

            var who = trigger.Who;
            var suggested = trigger.Suggested;

            _ = Task.Run(async () =>
            {
                NpcReply reply = null;
                try
                {
                    reply = await Player2Client.GenerateReplyAsync(prompt, null, "React now, in one line.")
                                               .ConfigureAwait(false);
                }
                catch { }

                var captured = reply;
                MainThread.Post(() =>
                {
                    try { Speak(who, captured, suggested); }
                    finally { RequestBudget.Finished(RequestBudget.Kind.Ambient); }
                });
            });
        }

        /// <summary>
        /// A deliberately small prompt. This is one line of reaction, not a conversation, and
        /// the size of the prompt is most of what decides how long the player waits for it.
        /// </summary>
        private static string BuildPrompt(Trigger trigger)
        {
            var s = ContextBuilder.Build(trigger.Who, false, null);
            var sb = new StringBuilder();

            sb.AppendLine("You are " + s.FullName + ", a citizen of a rain-soaked voxel noir city.");
            if (s.Traits.Count > 0) sb.AppendLine("You are " + string.Join(", ", s.Traits) + ".");
            if (!string.IsNullOrEmpty(s.Job)) sb.AppendLine("You work as " + s.Job + ".");
            sb.AppendLine();

            sb.AppendLine("# What just happened");
            sb.AppendLine(trigger.What);
            if (s.Bystanders.Count > 0)
                sb.AppendLine("Within earshot: " + string.Join(", ", s.Bystanders) + ".");
            else
                sb.AppendLine("Nobody is close by.");
            sb.AppendLine();

            sb.AppendLine("# How to answer");
            sb.AppendLine("Say one short line out loud, as this person, reacting to it. Reply with JSON only:");
            sb.AppendLine("{ \"speech\": \"...\", \"voice\": \"whisper\" or \"normal\" or \"shout\" }");
            // The situation carries a strong prior. Tested without it, a timid character
            // whispered while a fight broke out around her - defensible characterisation, but
            // it left the shout tier almost unused. The prior is stated and can still be
            // overridden by who the person is, which is the balance we want.
            sb.AppendLine("A moment like this is usually " + Voice.Describe(trigger.Suggested) + ".");
            sb.AppendLine("Follow that unless this particular person genuinely would not: fear, warnings and");
            sb.AppendLine("anything meant to carry are shouted, remarks to somebody standing beside you are");
            sb.AppendLine("whispered, everything else is normal.");
            sb.AppendLine("At most " + Mathf.Min(ModConfig.MaxReplyCharacters.Value, 120) + " characters. No stage directions.");

            return sb.ToString();
        }

        private static void Speak(Citizen who, NpcReply reply, VoiceLevel suggested)
        {
            if (who == null || reply == null || string.IsNullOrWhiteSpace(reply.Speech)) return;
            if (!CanBeHeardByPlayer(who)) return;   // they may have wandered off while it generated

            var level = Voice.Parse(reply.Voice, suggested);
            SpeechRelay.CitizenSaysAt(who, reply.Speech, level);

            LastLine = who.GetCitizenName() + " (" + Voice.Describe(level) + "): " + reply.Speech;
            SessionLog.Note("Ambient - " + LastLine);
        }

        private static bool CanBeHeardByPlayer(Citizen who)
        {
            try
            {
                var player = Player.Instance;
                if (player == null || who?.transform == null) return false;
                return Vector3.Distance(who.transform.position, player.transform.position)
                       <= ModConfig.ShoutRadius.Value;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeName(Actor actor)
        {
            try
            {
                var citizen = actor.TryCast<Citizen>();
                if (citizen != null) return citizen.GetCitizenName();
                return actor.isPlayer ? "the investigator" : null;
            }
            catch
            {
                return null;
            }
        }

        public static void Clear()
        {
            LastSeen.Clear();
            Pending.Clear();
            _playerHeld = "";
            _playerArmed = false;
            _watchingPlayerStarted = false;
        }
    }
}
