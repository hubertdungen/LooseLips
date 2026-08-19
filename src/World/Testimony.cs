using System;
using System.Collections.Generic;
using LooseLips.Core;

namespace LooseLips.World
{
    /// <summary>
    /// Turns a citizen giving something up in conversation into real evidence on your board.
    ///
    /// This deliberately does not invent a lead. The game already has a mechanism for a
    /// witness telling you where and when they saw somebody - <c>Human.RevealSighting</c> -
    /// and it is what produces a genuine, followable clue rather than a sentence that merely
    /// sounds like one. So the model does not get to write testimony; it only gets to decide
    /// whether the citizen is willing to give it. What they then say is what they actually
    /// saw, and it lands in the case file the same way a vanilla interrogation would.
    /// </summary>
    public static class Testimony
    {
        /// <summary>
        /// Have <paramref name="witness"/> tell the player about their sighting of the person
        /// named in <paramref name="targetName"/>. Returns null on success, or a reason.
        /// </summary>
        public static string RevealSighting(Citizen witness, string targetName)
        {
            if (!ModConfig.AllowTestimony.Value) return "giving up sightings is switched off";
            if (witness == null) return "no witness";
            if (string.IsNullOrWhiteSpace(targetName)) return "no name given";

            Human subject;
            var problem = FindSubject(witness, targetName, out subject);
            if (problem != null) return problem;

            try
            {
                // allowCalls false: this is a face to face admission, not them phoning it in.
                // allowGeneralClue true: if the specific sighting has gone stale they still give
                // up something usable, which is how a reluctant witness behaves anyway.
                witness.RevealSighting(subject, false, true, witness.speechController, true);
                return null;
            }
            catch (Exception e)
            {
                return "the game refused the testimony: " + e.Message;
            }
        }

        /// <summary>Who this witness could credibly testify about, for the prompt.</summary>
        public static List<string> PossibleSubjects(Citizen witness, int max = 6)
        {
            var names = new List<string>();
            if (witness == null) return names;

            try
            {
                if (witness.lastSightings == null) return names;
                foreach (var kv in witness.lastSightings)
                {
                    var who = kv.Key;
                    if (who == null) continue;
                    var name = who.GetCitizenName();
                    if (string.IsNullOrEmpty(name)) continue;
                    names.Add(name);
                    if (names.Count >= max) break;
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Could not list who they could testify about: " + e.Message);
            }
            return names;
        }

        /// <summary>
        /// Resolve a name to somebody this witness has genuinely seen. Refusing unknown names
        /// is what stops a confident model from producing testimony about a person who was
        /// never there.
        /// </summary>
        private static string FindSubject(Citizen witness, string targetName, out Human subject)
        {
            subject = null;
            var wanted = targetName.Trim();

            try
            {
                if (witness.lastSightings == null || witness.lastSightings.Count == 0)
                    return "they have not seen anybody worth mentioning";

                foreach (var kv in witness.lastSightings)
                {
                    var who = kv.Key;
                    if (who == null) continue;
                    var name = who.GetCitizenName();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        subject = who;
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                return "checking who they saw threw: " + e.Message;
            }

            return "they never saw that person";
        }
    }
}
