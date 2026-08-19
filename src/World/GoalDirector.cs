using System;
using System.Collections.Generic;
using System.Text;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Redirects what a citizen is actually trying to do, which is the difference between a
    /// conversation that changes their mood and one that changes their afternoon.
    ///
    /// Goals in this game are ScriptableObject presets held in <c>Toolbox.allGoals</c>, and
    /// their names live in Unity assets rather than in the assembly, so they cannot be
    /// hard-coded from the outside with any confidence. Instead each intent carries a list of
    /// keywords and is resolved against the real list at runtime. When nothing matches, the
    /// effect is refused with the intent named, and <see cref="DumpPresetNames"/> writes the
    /// actual catalogue to the transcript so the keywords can be corrected from evidence
    /// instead of guesswork.
    /// </summary>
    public static class GoalDirector
    {
        /// <summary>Intent name offered to the model, then the keywords that might match a preset.</summary>
        private static readonly Dictionary<string, string[]> Intents = new Dictionary<string, string[]>
        {
            { "go_home",   new[] { "gohome", "home", "returnhome", "gotohome" } },
            { "go_to_work", new[] { "work", "gotowork", "job" } },
            { "go_to_bed", new[] { "bed", "sleep", "gotobed" } },
            { "leave",     new[] { "leave", "exit", "gooutside", "wander", "walk" } },
        };

        public static IEnumerable<string> IntentNames() => Intents.Keys;

        /// <summary>
        /// Push a citizen towards an intent. Returns null when the goal was created, or a
        /// reason when it could not be.
        /// </summary>
        public static string Send(Citizen citizen, string intent)
        {
            if (!ModConfig.AllowGoalRedirection.Value) return "changing what people are doing is switched off";
            if (citizen == null || citizen.ai == null) return "no AI on this citizen";
            if (string.IsNullOrWhiteSpace(intent)) return "no destination given";

            string[] keywords;
            if (!Intents.TryGetValue(intent.Trim().ToLowerInvariant(), out keywords))
                return "not a destination this mod knows";

            var preset = FindPreset(keywords);
            if (preset == null)
                return "the game has no goal preset matching " + intent + " - run the goal dump in Debug";

            try
            {
                var now = SessionData.Instance != null ? SessionData.Instance.gameTime : 0f;
                var goal = citizen.ai.CreateNewGoal(preset, now, 0f);
                if (goal == null) return "the game refused to create the goal";

                // Priority is recalculated on its own schedule; nudging it now means the citizen
                // acts on this before finishing whatever they were already doing.
                try { goal.UpdatePriority(); } catch { }
                return null;
            }
            catch (Exception e)
            {
                return "creating the goal threw: " + e.Message;
            }
        }

        /// <summary>Send a citizen to go and look at where the player is standing.</summary>
        public static string InvestigateHere(Citizen citizen, bool urgent)
        {
            if (!ModConfig.AllowGoalRedirection.Value) return "changing what people are doing is switched off";
            if (citizen == null || citizen.ai == null) return "no AI on this citizen";

            var player = Player.Instance;
            if (player == null) return "no player to investigate";

            try
            {
                var node = player.currentNode;
                if (node == null) return "there is no node to send them to";

                citizen.ai.SetInvestigationUrgency(urgent
                    ? NewAIController.InvestigationUrgency.run
                    : NewAIController.InvestigationUrgency.walk);

                citizen.ai.Investigate(node, player.transform.position, null,
                    NewAIController.ReactionState.investigatingSound, 1f, 0, urgent);
                return null;
            }
            catch (Exception e)
            {
                return "sending them to look threw: " + e.Message;
            }
        }

        private static AIGoalPreset FindPreset(string[] keywords)
        {
            try
            {
                var all = Toolbox.Instance != null ? Toolbox.Instance.allGoals : null;
                if (all == null) return null;

                foreach (var keyword in keywords)
                {
                    foreach (var preset in all)
                    {
                        if (preset == null) continue;
                        var name = PresetName(preset);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.Replace(" ", "").Replace("_", "").ToLowerInvariant().Contains(keyword))
                            return preset;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Goal preset lookup failed: " + e.Message);
            }
            return null;
        }

        private static string PresetName(AIGoalPreset preset)
        {
            try
            {
                if (!string.IsNullOrEmpty(preset.presetName)) return preset.presetName;
            }
            catch { }
            try { return preset.name; } catch { return null; }
        }

        /// <summary>
        /// Write every goal preset the game actually has into the transcript. The keyword lists
        /// above are a guess until this has been run once against a loaded save.
        /// </summary>
        public static string DumpPresetNames()
        {
            try
            {
                var all = Toolbox.Instance != null ? Toolbox.Instance.allGoals : null;
                if (all == null) return "Toolbox has no goal list yet - load a save first.";

                var names = new List<string>();
                foreach (var preset in all)
                {
                    var n = PresetName(preset);
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                names.Sort();

                var sb = new StringBuilder();
                sb.AppendLine("Goal presets the game has (" + names.Count + "):");
                foreach (var n in names) sb.AppendLine("  " + n);
                SessionLog.Note(sb.ToString());

                var matched = new List<string>();
                foreach (var pair in Intents)
                {
                    var found = FindPreset(pair.Value);
                    matched.Add(pair.Key + " -> " + (found != null ? PresetName(found) : "NOTHING MATCHED"));
                }
                SessionLog.Note("Intent mapping: " + string.Join(", ", matched));

                return names.Count + " presets written to the transcript.";
            }
            catch (Exception e)
            {
                return "Dump failed: " + e.Message;
            }
        }
    }
}
