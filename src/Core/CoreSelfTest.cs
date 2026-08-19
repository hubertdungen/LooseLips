using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LooseLips.Context;
using LooseLips.Dialog;
using LooseLips.Player2;
using LooseLips.World;
using UnityEngine;

namespace LooseLips.Core
{
    /// <summary>
    /// Walks the entire core path against a real citizen standing in front of you and reports
    /// which link broke.
    ///
    /// The mod has seven things that must all work for one sentence to matter - finding the
    /// person, reading what they know, building the prompt, reaching the model, parsing the
    /// answer, speaking it, and applying the consequences - and in game a failure in any of
    /// them looks the same: someone shrugs at you. This runs them in order, one at a time,
    /// and names the first one that fails.
    /// </summary>
    public static class CoreSelfTest
    {
        private const string Probe = "I know what you did last night. Tell me about it, and be quick.";

        public static bool Running { get; private set; }

        /// <summary>Last result, kept for the settings window to display.</summary>
        public static string LastSummary { get; private set; } = "Not run yet.";

        public static void Run()
        {
            if (Running) return;
            Running = true;
            LastSummary = "Running...";

            var report = new Report();

            Citizen subject = null;
            CitizenSnapshot snapshot = null;
            string systemPrompt = null;
            string turnMessage = null;

            try
            {
                var player = Player.Instance;
                if (!report.Check("player", player != null, "found", "there is no player - are you in a game?"))
                {
                    Finish(report);
                    return;
                }

                subject = NearestCitizen(player);
                if (!report.Check("someone to talk to", subject != null,
                        () => subject.GetCitizenName() + ", " + Distance(player, subject).ToString("0.0") + " m away",
                        "nobody within shouting distance - stand near someone and try again"))
                {
                    Finish(report);
                    return;
                }

                snapshot = ContextBuilder.Build(subject, false, null);
                report.Check("what they know", snapshot != null,
                    () => snapshot.Traits.Count + " traits, " + snapshot.GroundTruth.Count + " facts they actually know, " +
                          snapshot.Bystanders.Count + " within earshot",
                    "the snapshot could not be built");

                if (snapshot != null && snapshot.GroundTruth.Count == 0)
                    report.Note("They know nothing worth saying, so this test cannot show a secret being given up. " +
                                "Try again next to someone tied to the case.");

                systemPrompt = PromptBuilder.BuildSystemPrompt(snapshot);
                turnMessage = PromptBuilder.BuildTurnMessage(snapshot, Probe);
                report.Check("the prompt", !string.IsNullOrWhiteSpace(systemPrompt),
                    () => systemPrompt.Length + " + " + turnMessage.Length + " characters, " +
                          snapshot.PermittedEffects.Count + " effects offered",
                    "the prompt came out empty");

                var talk = Earshot.CitizensWhoCanHear(player, false).Count;
                var shout = Earshot.CitizensWhoCanHear(player, true).Count;
                report.Check("voice reach", true, () => "speaking reaches " + talk + ", shouting reaches " + shout, null);
                report.Note(EffectFeasibility(subject));
            }
            catch (Exception e)
            {
                report.Fail("setting up", e.Message);
                Finish(report);
                return;
            }

            var citizen = subject;
            var sys = systemPrompt;
            var turn = turnMessage;

            _ = Task.Run(async () =>
            {
                NpcReply reply = null;
                var reachable = false;
                try
                {
                    reachable = await Player2Client.ProbeAsync().ConfigureAwait(false);
                    if (reachable)
                        reply = await Player2Client.GenerateReplyAsync(sys, null, turn).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    MainThread.Post(() => report.Fail("reaching the model", e.Message));
                }

                MainThread.Post(() =>
                {
                    try
                    {
                        FinishOnMainThread(report, citizen, reachable, reply);
                    }
                    finally
                    {
                        Finish(report);
                    }
                });
            });
        }

        private static void FinishOnMainThread(Report report, Citizen citizen, bool reachable, NpcReply reply)
        {
            if (!report.Check("the Player2 app", reachable, "answering",
                    "no answer from " + ModConfig.BaseUrl.Value + " - is the app running?"))
                return;

            if (!report.Check("a reply", reply != null && !string.IsNullOrWhiteSpace(reply.Speech),
                    () => reply.LatencyMs + " ms",
                    reply == null ? "nothing came back" : "empty after " + reply.LatencyMs + " ms: " + reply.Raw))
                return;

            report.Check("the reply schema", reply.WellFormed,
                () => "clean JSON, truthfulness " + reply.Truthfulness.ToString("0.00"),
                "the model answered in prose, so this turn can carry no consequences at all");

            report.Note("They said: " + reply.Speech);

            try
            {
                SpeechRelay.CitizenSays(citizen, reply.Speech, false);
                report.Check("the speech pipeline", true, "line handed to the game", null);
            }
            catch (Exception e)
            {
                report.Fail("the speech pipeline", e.Message);
            }

            try
            {
                var effects = WorldEffectExecutor.Apply(citizen, reply, false);
                var did = effects.Applied.Count > 0 ? string.Join(", ", effects.Applied) : "nothing";
                report.Check("consequences", true, () => did, null);
                if (effects.Rejected.Count > 0)
                    report.Note("Refused: " + string.Join("; ", effects.Rejected));
            }
            catch (Exception e)
            {
                report.Fail("consequences", e.Message);
            }
        }

        /// <summary>
        /// Which effects could physically happen right now. Saves chasing a bug when the real
        /// answer is that the person you picked has empty hands and no police nearby.
        /// </summary>
        private static string EffectFeasibility(Citizen c)
        {
            var possible = new List<string>();
            var not = new List<string>();

            try
            {
                var holding = c.rightHandInteractable ?? c.leftHandInteractable;
                (holding != null ? possible : not).Add("give_item");

                var officers = 0;
                foreach (var other in Earshot.CitizensWhoCanHear(c, true))
                {
                    try { if (other != null && other.isEnforcer && !other.isDead) officers++; } catch { }
                }
                (officers > 0 ? possible : not).Add("call_police/protect/accuse");

                (c.isHome ? possible : not).Add("answer_door");
                (c.ai != null && !c.ai.restrained ? possible : not).Add("flee/attack");
            }
            catch (Exception e)
            {
                return "Could not work out which effects are possible: " + e.Message;
            }

            return "Possible on this person right now: " + (possible.Count > 0 ? string.Join(", ", possible) : "none") +
                   ". Not possible: " + (not.Count > 0 ? string.Join(", ", not) : "none") + ".";
        }

        private static Citizen NearestCitizen(Player player)
        {
            Citizen best = null;
            var bestDistance = float.MaxValue;

            foreach (var c in Earshot.CitizensWhoCanHear(player, true))
            {
                if (c == null) continue;
                var d = Distance(player, c);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = c;
                }
            }
            return best;
        }

        private static float Distance(Actor a, Actor b)
        {
            try { return Vector3.Distance(a.transform.position, b.transform.position); }
            catch { return float.MaxValue; }
        }

        private static void Finish(Report report)
        {
            Running = false;
            LastSummary = report.Summary();
            SessionLog.Note(Environment.NewLine + report.Full());
            Plugin.Log.LogInfo(report.Full());
        }

        /// <summary>Accumulates the staged result so it can be shown and written in one piece.</summary>
        private sealed class Report
        {
            private readonly StringBuilder _lines = new StringBuilder("Loose Lips self-test" + Environment.NewLine);
            private int _passed;
            private string _firstFailure;

            public bool Check(string stage, bool ok, string detail, string failure)
                => Check(stage, ok, () => detail, failure);

            public bool Check(string stage, bool ok, Func<string> detail, string failure)
            {
                if (ok)
                {
                    _passed++;
                    var text = "";
                    try { text = detail != null ? detail() : ""; } catch { }
                    _lines.AppendLine("  ok    " + stage + (string.IsNullOrEmpty(text) ? "" : ": " + text));
                }
                else
                {
                    _firstFailure ??= stage;
                    _lines.AppendLine("  FAIL  " + stage + (string.IsNullOrEmpty(failure) ? "" : ": " + failure));
                }
                return ok;
            }

            public void Fail(string stage, string why)
            {
                _firstFailure ??= stage;
                _lines.AppendLine("  FAIL  " + stage + ": " + why);
            }

            public void Note(string text)
            {
                if (!string.IsNullOrWhiteSpace(text)) _lines.AppendLine("        " + text);
            }

            public string Full() => _lines.ToString().TrimEnd();

            public string Summary()
                => _firstFailure == null
                    ? "All " + _passed + " stages passed. See the transcript for the exchange."
                    : "Stopped at: " + _firstFailure + ". See the transcript.";
        }
    }
}
