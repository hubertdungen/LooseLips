using System;
using System.Collections.Generic;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Effects that land on everybody who heard, not just the person you were talking to.
    ///
    /// This is the half of the design that shouting exists for. A line delivered to one
    /// citizen changes one citizen; a line delivered to a room should be able to empty it.
    /// Reach is never assumed - every one of these walks the same earshot calculation the
    /// rest of the mod uses, so a whisper in an empty alley moves nobody no matter how
    /// dramatic the model thinks it was being.
    /// </summary>
    public static class CrowdEffects
    {
        /// <summary>Everyone who heard, excluding the person who spoke.</summary>
        private static List<Citizen> Audience(Citizen speaker, bool shouted)
        {
            var crowd = new List<Citizen>();
            foreach (var c in Earshot.CitizensWhoCanHear(speaker, shouted))
            {
                if (c == null) continue;
                if (speaker != null && c.humanID == speaker.humanID) continue;
                crowd.Add(c);
            }
            return crowd;
        }

        /// <summary>Scatter everybody in earshot.</summary>
        public static string Panic(Citizen speaker, bool shouted)
        {
            if (!ModConfig.AllowCrowdEffects.Value) return "crowd effects are switched off";
            if (!ModConfig.AllowCombatEffects.Value) return "fleeing and combat are switched off";

            var crowd = Audience(speaker, shouted);
            if (crowd.Count == 0) return "nobody else heard it";

            var moved = 0;
            foreach (var c in crowd)
            {
                try
                {
                    if (c.ai == null || c.ai.restrained) continue;
                    c.ai.CancelCombat();
                    c.ai.inFleeState = true;
                    c.ai.TriggerReactionIndicator();
                    moved++;
                }
                catch { }
            }
            return moved > 0 ? null : "nobody in earshot could run";
        }

        /// <summary>Settle everybody in earshot.</summary>
        public static string Settle(Citizen speaker, bool shouted)
        {
            if (!ModConfig.AllowCrowdEffects.Value) return "crowd effects are switched off";

            var crowd = Audience(speaker, shouted);
            if (crowd.Count == 0) return "nobody else heard it";

            var cap = ModConfig.MaxSuspicionShiftPerLine.Value;
            var moved = 0;
            foreach (var c in crowd)
            {
                try
                {
                    if (c.ai == null) continue;
                    c.ai.inFleeState = false;
                    c.ai.alertness = Mathf.Clamp01(c.ai.alertness - cap);
                    moved++;
                }
                catch { }
            }
            return moved > 0 ? null : "nobody in earshot could be calmed";
        }

        /// <summary>Draw everybody in earshot over to see what the noise was.</summary>
        public static string Gather(Citizen speaker, bool shouted)
        {
            if (!ModConfig.AllowCrowdEffects.Value) return "crowd effects are switched off";
            if (!ModConfig.AllowGoalRedirection.Value) return "changing what people are doing is switched off";

            var crowd = Audience(speaker, shouted);
            if (crowd.Count == 0) return "nobody else heard it";

            var moved = 0;
            foreach (var c in crowd)
            {
                if (GoalDirector.InvestigateHere(c, shouted) == null) moved++;
            }
            return moved > 0 ? null : "nobody in earshot could come and look";
        }

        /// <summary>How many people a line would land on, for the prompt and the reach meter.</summary>
        public static int Size(Citizen speaker, bool shouted) => Audience(speaker, shouted).Count;
    }
}
