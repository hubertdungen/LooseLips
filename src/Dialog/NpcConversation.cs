using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LooseLips.Context;
using LooseLips.Core;
using LooseLips.Player2;
using LooseLips.World;
using UnityEngine;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Citizens talking to each other, and information actually moving between them as a result.
    ///
    /// Two decisions shape this. First, an exchange is generated as one request rather than a
    /// turn each: two citizens trading four lines would otherwise cost four round trips at
    /// several seconds apiece, and the pair would still be standing there when it finished.
    /// Second, it only runs where the player can hear. That is not a saving so much as the
    /// point - a conversation you cannot overhear has no gameplay in it, and generating it
    /// would spend real time on something nobody will ever see.
    ///
    /// What makes it more than ambience is the gossip: when one of them says they saw somebody,
    /// the other genuinely learns it through the same sighting record the game uses. Tell a
    /// barman something worth repeating and it can reach the person you wanted it to reach.
    /// </summary>
    public static class NpcConversation
    {
        private static float _nextAttempt;
        private static bool _busy;

        /// <summary>Pairs already talked to recently, so the same two do not loop forever.</summary>
        private static readonly Dictionary<long, float> Cooldowns = new Dictionary<long, float>();

        public static string LastExchange { get; private set; } = "None yet.";

        public static void Tick()
        {
            if (!ModConfig.EnableNpcConversations.Value) return;
            if (_busy) return;
            if (Time.time < _nextAttempt) return;

            _nextAttempt = Time.time + ModConfig.NpcConversationInterval.Value;

            try
            {
                Citizen a, b;
                if (!FindPair(out a, out b)) return;
                Begin(a, b);
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Looking for a conversation to start failed: " + e.Message);
            }
        }

        /// <summary>
        /// Two citizens the player could overhear, in the same room, who have not just spoken.
        /// </summary>
        private static bool FindPair(out Citizen a, out Citizen b)
        {
            a = null;
            b = null;

            var player = Player.Instance;
            if (player == null) return false;

            var nearby = Earshot.CitizensWhoCanHear(player, false);
            if (nearby.Count < 2) return false;

            for (var i = 0; i < nearby.Count; i++)
            {
                for (var j = i + 1; j < nearby.Count; j++)
                {
                    var first = nearby[i];
                    var second = nearby[j];
                    if (first == null || second == null) continue;
                    if (!Suitable(first) || !Suitable(second)) continue;

                    try
                    {
                        if (first.currentRoom == null || second.currentRoom == null) continue;
                        if (first.currentRoom.roomID != second.currentRoom.roomID) continue;
                        if (Vector3.Distance(first.transform.position, second.transform.position) >
                            ModConfig.TalkRadius.Value) continue;
                    }
                    catch { continue; }

                    if (OnCooldown(first, second)) continue;

                    a = first;
                    b = second;
                    return true;
                }
            }
            return false;
        }

        private static bool Suitable(Citizen c)
        {
            try
            {
                if (c.isDead || c.isAsleep || c.isStunned) return false;
                if (c.ai == null) return false;
                if (c.ai.inCombat || c.ai.inFleeState || c.ai.restrained) return false;
                if (ConversationOrchestrator.IsBusy(c)) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long PairKey(Citizen a, Citizen b)
        {
            var lo = Math.Min(a.humanID, b.humanID);
            var hi = Math.Max(a.humanID, b.humanID);
            return ((long)lo << 32) | (uint)hi;
        }

        private static bool OnCooldown(Citizen a, Citizen b)
        {
            var key = PairKey(a, b);
            float until;
            if (Cooldowns.TryGetValue(key, out until) && Time.time < until) return true;
            return false;
        }

        private static void Begin(Citizen a, Citizen b)
        {
            _busy = true;
            Cooldowns[PairKey(a, b)] = Time.time + ModConfig.NpcConversationCooldown.Value;

            string prompt;
            try
            {
                prompt = BuildPrompt(a, b);
            }
            catch (Exception e)
            {
                _busy = false;
                Plugin.Log.LogWarning("Could not build a conversation prompt: " + e.Message);
                return;
            }

            _ = Task.Run(async () =>
            {
                NpcReply raw = null;
                try
                {
                    raw = await Player2Client.GenerateReplyAsync(prompt, null, "Write the exchange now.")
                                             .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    MainThread.Post(() => Plugin.Log.LogWarning("Conversation generation failed: " + e.Message));
                }

                var captured = raw;
                MainThread.Post(() =>
                {
                    try
                    {
                        Play(a, b, captured);
                    }
                    finally
                    {
                        _busy = false;
                    }
                });
            });
        }

        private static string BuildPrompt(Citizen a, Citizen b)
        {
            var sb = new StringBuilder();
            var sa = ContextBuilder.Build(a, false, null);
            var sb2 = ContextBuilder.Build(b, false, null);

            sb.AppendLine("Write a short overheard exchange between two citizens of a rain-soaked voxel noir city.");
            sb.AppendLine("They are talking to each other, not to the player. Nobody is being interviewed.");
            sb.AppendLine();

            sb.AppendLine("# " + sa.FullName);
            if (!string.IsNullOrEmpty(sa.Job)) sb.AppendLine("Work: " + sa.Job);
            if (sa.Traits.Count > 0) sb.AppendLine("Traits: " + string.Join(", ", sa.Traits));
            if (sa.GroundTruth.Count > 0) sb.AppendLine("Knows: " + string.Join(" ", sa.GroundTruth));
            sb.AppendLine();

            sb.AppendLine("# " + sb2.FullName);
            if (!string.IsNullOrEmpty(sb2.Job)) sb.AppendLine("Work: " + sb2.Job);
            if (sb2.Traits.Count > 0) sb.AppendLine("Traits: " + string.Join(", ", sb2.Traits));
            if (sb2.GroundTruth.Count > 0) sb.AppendLine("Knows: " + string.Join(" ", sb2.GroundTruth));
            sb.AppendLine();

            sb.AppendLine("# Where they are");
            if (!string.IsNullOrEmpty(sa.LocationName)) sb.AppendLine(sa.LocationName + ", " + sa.TimeOfDay);
            sb.AppendLine("A private investigator is standing close enough to overhear them, which they have " +
                          "not noticed. Do not have them address that person.");
            sb.AppendLine();

            sb.AppendLine("# How to answer");
            sb.AppendLine("Reply with a single JSON object and nothing else:");
            sb.AppendLine("{");
            sb.AppendLine("  \"lines\": [ { \"who\": \"first or second\", \"says\": \"one short line\" } ],");
            sb.AppendLine("  \"gossip\": { \"teller\": \"first or second\", \"about\": \"full name of a person mentioned\" }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Between two and " + ModConfig.NpcConversationLines.Value + " lines, alternating.");
            sb.AppendLine("Clipped, period-appropriate, ordinary. Small talk, complaints, rumours.");
            sb.AppendLine("Include gossip only if one of them genuinely mentions having seen a named person.");
            sb.AppendLine("Only these names may be mentioned as having been seen:");

            var seenA = Testimony.PossibleSubjects(a, 4);
            var seenB = Testimony.PossibleSubjects(b, 4);
            if (seenA.Count == 0 && seenB.Count == 0) sb.AppendLine("  nobody - leave gossip out entirely.");
            else
            {
                foreach (var n in seenA) sb.AppendLine("  " + n + " (seen by first)");
                foreach (var n in seenB) sb.AppendLine("  " + n + " (seen by second)");
            }

            return sb.ToString();
        }

        private static void Play(Citizen a, Citizen b, NpcReply raw)
        {
            if (raw == null || string.IsNullOrWhiteSpace(raw.Raw)) return;

            NpcExchange exchange = null;
            try
            {
                exchange = Player2Client.ParseExchange(raw.Raw);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the exchange: " + e.Message);
            }

            if (exchange == null || exchange.Lines == null || exchange.Lines.Count == 0)
            {
                SessionLog.Note("Overheard exchange discarded: nothing usable came back.");
                return;
            }

            var transcript = new StringBuilder();
            var max = Mathf.Min(exchange.Lines.Count, ModConfig.NpcConversationLines.Value);

            for (var i = 0; i < max; i++)
            {
                var line = exchange.Lines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Says)) continue;

                var speaker = IsFirst(line.Who) ? a : b;
                var listener = IsFirst(line.Who) ? b : a;

                // Each line is delayed a beat past the last so they take turns rather than
                // talking over one another; the game will not space them out on its own.
                var delay = i * ModConfig.NpcConversationLineGap.Value;
                var text = line.Says;
                var s = speaker;
                var l = listener;
                DelayedSpeech.Queue(delay, () => SpeechRelay.CitizenSaysTo(s, l, text));

                transcript.AppendLine("  " + speaker.GetCitizenName() + ": " + text);
            }

            var gossiped = ApplyGossip(a, b, exchange.Gossip);

            LastExchange = a.GetCitizenName() + " and " + b.GetCitizenName() +
                           (gossiped != null ? ", who passed on " + gossiped : "");
            SessionLog.Note("Overheard - " + a.GetCitizenName() + " and " + b.GetCitizenName() +
                            Environment.NewLine + transcript.ToString().TrimEnd() +
                            (gossiped != null ? Environment.NewLine + "  gossip: " + gossiped : ""));
        }

        private static bool IsFirst(string who)
            => string.IsNullOrEmpty(who) || who.Trim().ToLowerInvariant().StartsWith("f");

        /// <summary>
        /// Move a sighting from the one who has it to the one who does not. This is the part
        /// with teeth: afterwards the listener can be asked about it and will genuinely know.
        /// </summary>
        private static string ApplyGossip(Citizen a, Citizen b, GossipItem gossip)
        {
            if (!ModConfig.NpcGossipSpreads.Value) return null;
            if (gossip == null || string.IsNullOrWhiteSpace(gossip.About)) return null;

            try
            {
                var teller = IsFirst(gossip.Teller) ? a : b;
                var listener = IsFirst(gossip.Teller) ? b : a;

                if (teller.lastSightings == null) return null;

                foreach (var kv in teller.lastSightings)
                {
                    var subject = kv.Key;
                    if (subject == null) continue;
                    var name = subject.GetCitizenName();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.IndexOf(gossip.About.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // isSound 0, phoneCall false: they heard it from a person standing in front of them.
                    listener.UpdateLastSighting(subject, false, 0);
                    return name + ", from " + teller.GetCitizenName() + " to " + listener.GetCitizenName();
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Passing gossip on failed: " + e.Message);
            }
            return null;
        }
    }

    /// <summary>
    /// A tiny timer queue, because speech has to be spaced out and the game's own dialogue
    /// scheduling is not reachable from here without joining a queue we do not control.
    /// </summary>
    public static class DelayedSpeech
    {
        private static readonly List<KeyValuePair<float, Action>> Pending = new List<KeyValuePair<float, Action>>();

        public static void Queue(float seconds, Action action)
        {
            if (action == null) return;
            Pending.Add(new KeyValuePair<float, Action>(Time.time + seconds, action));
        }

        public static void Tick()
        {
            if (Pending.Count == 0) return;

            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < Pending[i].Key) continue;
                var action = Pending[i].Value;
                Pending.RemoveAt(i);
                try { action(); }
                catch (Exception e) { Plugin.Log.LogWarning("A delayed line threw: " + e.Message); }
            }
        }

        public static void Clear() => Pending.Clear();
    }
}
