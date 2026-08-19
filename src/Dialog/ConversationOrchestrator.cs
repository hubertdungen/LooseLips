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
        /// <summary>Citizens with a request currently in flight, so we do not stack turns.</summary>
        private static readonly HashSet<int> Busy = new HashSet<int>();

        public static bool IsBusy(Citizen citizen)
            => citizen != null && Busy.Contains(citizen.humanID);

        /// <summary>
        /// Begin an exchange. Returns immediately; the reply arrives later on the main thread.
        /// </summary>
        public static void Speak(Citizen citizen, string playerLine, bool shouted, string vanillaLine = null)
        {
            if (citizen == null || string.IsNullOrWhiteSpace(playerLine)) return;

            var id = citizen.humanID;
            if (!Busy.Add(id))
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogInfo("Ignoring a second line while " + citizen.GetCasualName() + " is still thinking.");
                return;
            }

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
                    reply = await Player2Client.GenerateReplyAsync(systemPrompt, history, turnMessage)
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
