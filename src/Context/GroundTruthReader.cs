using System;
using LooseLips.Core;

namespace LooseLips.Context
{
    /// <summary>
    /// Collects what a citizen actually knows, so the model has real facts to either
    /// hand over or lie about. Nothing here is invented: every line traces to game state.
    ///
    /// Keeping truth separate from persona is what makes deception meaningful. The model
    /// is told it may withhold or distort these facts, but never substitute new ones,
    /// so a lie stays a lie about something real.
    /// </summary>
    public static class GroundTruthReader
    {
        public static void Fill(Citizen citizen, CitizenSnapshot s)
        {
            if (citizen == null || s == null) return;

            Add(s, () =>
            {
                if (citizen.home == null) return null;
                return "I live at " + citizen.home.name + ".";
            });

            Add(s, () =>
            {
                if (citizen.job == null) return null;
                var where = citizen.job.employer != null ? citizen.job.employer.name : "somewhere in the city";
                return "I work as " + citizen.job.name + " at " + where + ".";
            });

            Add(s, () =>
            {
                if (citizen.partner == null) return null;
                return "My partner is " + citizen.partner.GetCitizenName() + ".";
            });

            // Their own door code. A well-argued case can get this out of them; the
            // executor still checks the passcode exists before it counts as handed over.
            Add(s, () =>
            {
                if (citizen.passcode == null) return null;
                var digits = citizen.passcode.GetDigits();
                if (digits == null || digits.Count == 0) return null;
                var code = "";
                foreach (var d in digits) code += d.ToString();
                return "The door code to my home is " + code + ".";
            });

            // Who they know, ranked by how well. Caps at a handful so the prompt stays small.
            Add(s, () =>
            {
                if (citizen.acquaintances == null) return null;
                var names = new System.Collections.Generic.List<string>();
                foreach (var acq in citizen.acquaintances)
                {
                    if (acq == null) continue;
                    if (acq.known < 0.4f) continue;
                    var other = acq.GetOther(citizen);
                    if (other == null) continue;
                    names.Add(other.GetCitizenName());
                    if (names.Count >= 6) break;
                }
                if (names.Count == 0) return null;
                return "People I know well: " + string.Join(", ", names) + ".";
            });

            // Recent sightings are the backbone of "have you seen anyone".
            Add(s, () =>
            {
                if (citizen.lastSightings == null || citizen.lastSightings.Count == 0) return null;
                var seen = new System.Collections.Generic.List<string>();
                foreach (var kv in citizen.lastSightings)
                {
                    var who = kv.Key;
                    var sighting = kv.Value;
                    if (who == null || sighting == null) continue;
                    string when = null;
                    try { when = SessionData.Instance.TimeAndDate(sighting.time, true, true, false); } catch { }
                    seen.Add(who.GetCitizenName() + (when != null ? " (" + when + ")" : ""));
                    if (seen.Count >= 5) break;
                }
                if (seen.Count == 0) return null;
                return "People I have seen recently: " + string.Join("; ", seen) + ".";
            });

            // Being the killer changes everything about how they answer.
            Add(s, () =>
            {
                try
                {
                    var mc = MurderController.Instance;
                    if (mc == null || mc.currentMurderer == null) return null;
                    if (mc.currentMurderer.humanID != citizen.humanID) return null;
                    return "SECRET: I am the murderer the player is hunting. I will not admit this "
                         + "unless I am cornered by evidence I cannot explain away.";
                }
                catch
                {
                    return null;
                }
            });
        }

        private static void Add(CitizenSnapshot s, Func<string> producer)
        {
            try
            {
                var line = producer();
                if (!string.IsNullOrWhiteSpace(line)) s.GroundTruth.Add(line);
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                {
                    Plugin.Log.LogWarning("Ground truth item failed: " + e.Message);
                }
            }
        }
    }
}
