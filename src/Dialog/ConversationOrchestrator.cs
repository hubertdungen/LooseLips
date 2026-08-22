using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LooseLips.Context;
using LooseLips.Core;
using LooseLips.Player2;
using LooseLips.World;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Drives one exchange end to end: snapshot the world, ask the model, then speak the
    /// answer and apply whatever it earned.
    ///
    /// The shape here is dictated by two hard constraints. Unity is single threaded, so
    /// all game reads and writes happen on the main thread and only the HTTP call runs
    /// off it. And the game does not pause during dialogue, so the citizen shows a
    /// thinking beat while the request is in flight rather than the world freezing.
    /// </summary>
    public static class ConversationOrchestrator
    {
        /// <summary>
        /// Citizens with a request in flight, and when it started, so we do not stack turns.
        ///
        /// The time matters. Being busy is what hides "Say something..." from a citizen, so an
        /// entry that is never cleared does not delay a conversation - it ends every future one
        /// with that person, permanently, with no message and nothing in the log. Anything that
        /// could strand an entry here (a request the app never answers, a scene change while one
        /// is in flight, a queued callback that never ran) has the same symptom, so rather than
        /// chase each cause the entry is treated as stale once no reply could still be coming.
        /// </summary>
        private static readonly Dictionary<int, float> Busy = new Dictionary<int, float>();

        /// <summary>Past this, no reply can still be on its way: the request timeout, three
        /// attempts of it, and room for the retry window on top.</summary>
        private static float StuckAfterSeconds
            => ModConfig.RequestTimeoutSeconds.Value * 3f + 15f;

        public static bool IsBusy(Citizen citizen)
        {
            if (citizen == null) return false;

            float since;
            if (!Busy.TryGetValue(citizen.humanID, out since)) return false;

            if (UnityEngine.Time.realtimeSinceStartup - since < StuckAfterSeconds) return true;

            Busy.Remove(citizen.humanID);
            Plugin.Log.LogWarning(citizen.GetCasualName() + " was still marked as thinking after "
                + (int)StuckAfterSeconds + "s. Clearing it - a reply that late is never coming, "
                + "and leaving it would have hidden the option for the rest of the session.");
            return false;
        }

        /// <summary>
        /// Begin an exchange. Returns immediately; the reply arrives later on the main thread.
        /// </summary>
        public static void Speak(Citizen citizen, string playerLine, bool shouted, string vanillaLine = null)
        {
            if (citizen == null || string.IsNullOrWhiteSpace(playerLine)) return;

            var id = citizen.humanID;
            if (IsBusy(citizen))
            {
                Plugin.Log.LogInfo("Ignoring a second line while " + citizen.GetCasualName()
                                 + " is still thinking about the first.");
                return;
            }

            Busy[id] = UnityEngine.Time.realtimeSinceStartup;

            CitizenSnapshot snapshot;
            string systemPrompt;
            string turnMessage;
            IReadOnlyList<ChatMessage> history;

            try
            {
                snapshot = ContextBuilder.Build(citizen, shouted, vanillaLine);
                systemPrompt = PromptBuilder.BuildSystemPrompt(snapshot);
                turnMessage = PromptBuilder.BuildTurnMessage(snapshot, playerLine);
                history = ConversationMemory.Get(id);
            }
            catch (Exception e)
            {
                Busy.Remove(id);
                Plugin.Log.LogError("Could not build conversation context: " + e);
                return;
            }

            // Let the world know the player said something out loud, whatever the model returns.
            SpeechRelay.PlayerSaid(citizen, playerLine, shouted);
            SpeechRelay.ShowThinking(citizen);

            var earshot = 0;
            try { earshot = World.Earshot.CitizensWhoCanHear(citizen, shouted).Count; } catch { }

            _ = Task.Run(async () =>
            {
                NpcReply reply = null;
                try
                {
                    // The player typed this line themselves and is standing there waiting for an
                    // answer. If the model stops mid-object, ask again rather than making them
                    // type it a second time; the citizen's own turns are not rationed.
                    reply = await Player2Client.GenerateReplyAsync(systemPrompt, history, turnMessage,
                                                                  retryIfUnusable: true)
                                               .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    MainThread.Post(() => Plugin.Log.LogError("Generation failed: " + e.Message));
                }

                MainThread.Post(() =>
                {
                    try
                    {
                        Complete(citizen, playerLine, shouted, reply, earshot, systemPrompt, turnMessage);
                    }
                    finally
                    {
                        Busy.Remove(id);
                    }
                });
            });
        }

        private static void Complete(Citizen citizen, string playerLine, bool shouted, NpcReply reply,
                                    int earshot, string systemPrompt, string turnMessage)
        {
            if (citizen == null) return;

            var who = citizen.GetCitizenName();

            if (reply == null || string.IsNullOrWhiteSpace(reply.Speech))
            {
                SpeechRelay.ShowUnavailable(citizen);
                SessionLog.Exchange(who, shouted, earshot, playerLine,
                    reply != null ? reply.LatencyMs : 0, reply != null ? reply.Raw : null,
                    null, 0f, 0f, null, null, null, systemPrompt, turnMessage);
                return;
            }

            SpeechRelay.CitizenSays(citizen, reply.Speech, shouted);
            ConversationMemory.Record(citizen.humanID, playerLine, reply.Speech);

            WorldEffectExecutor.EffectReport report = null;
            try
            {
                report = WorldEffectExecutor.Apply(citizen, reply, shouted);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Applying world effects failed: " + e);
            }

            SessionLog.Exchange(who, shouted, earshot, playerLine, reply.LatencyMs, reply.Raw,
                reply.Speech, reply.Truthfulness, reply.Alarm, reply.Reason,
                report?.Applied, report?.Rejected, systemPrompt, turnMessage);

            if (!reply.WellFormed)
            {
                // The line still gets spoken, but nothing structured came with it: no effects, no
                // relationship movement. Worth saying out loud, because the symptom in game is a
                // citizen who talks well and never does anything.
                Plugin.Log.LogWarning("The model ignored the reply schema for " + who +
                                      ", so this turn could not carry any consequences.");
            }

            if (ModConfig.VerboseLogging.Value)
            {
                var effects = report != null && report.Applied.Count > 0 ? string.Join(", ", report.Applied) : "none";
                var refused = report != null && report.Rejected.Count > 0 ? string.Join(", ", report.Rejected) : "none";
                Plugin.Log.LogInfo(
                    citizen.GetCasualName() + " replied in " + reply.LatencyMs + " ms (truthfulness " +
                    reply.Truthfulness.ToString("0.00") + ", alarm " + reply.Alarm.ToString("0.00") +
                    "). Applied: " + effects + ". Refused: " + refused +
                    (string.IsNullOrWhiteSpace(reply.Reason) ? "" : " | reasoning: " + reply.Reason));
            }

            // Anyone else in earshot reacts to what they just overheard.
            try
            {
                BystanderReactions.Propagate(citizen, reply, shouted);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bystander propagation failed: " + e.Message);
            }

            if (ModConfig.EnableTts.Value)
            {
                var line = reply.Speech;
                _ = Player2Client.SpeakAsync(line);
            }
        }
    }
}
