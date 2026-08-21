using System;
using System.Collections.Generic;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Turning one citizen against another, or towards them, and getting somebody to stand up
    /// for a third party.
    ///
    /// Everything else in this mod moves the line between you and the person in front of you.
    /// This is the first thing that changes a relationship you are not in. It is also the
    /// easiest place to let a model rewrite the city by accident, so it is fenced hard: you can
    /// only move an opinion about somebody the speaker actually knows or can see, the shift is
    /// capped exactly like the one about you, and poisoning a friendship is deliberately harder
    /// than nudging an acquaintance - people do not drop a friend of twenty years because a
    /// stranger said something in the street.
    /// </summary>
    public static class Opinion
    {
        /// <summary>
        /// Move how <paramref name="speaker"/> feels about the person named. Positive warms,
        /// negative sours. Returns null when it happened, or a reason.
        /// </summary>
        public static string Shift(Citizen speaker, string targetName, float delta, bool shouted)
        {
            if (!ModConfig.AllowThirdPartyOpinion.Value) return "changing how people see each other is switched off";
            if (speaker == null) return "nobody to persuade";
            if (string.IsNullOrWhiteSpace(targetName)) return "no name given";

            Human target;
            var problem = Resolve(speaker, targetName, shouted, out target);
            if (problem != null) return problem;

            try
            {
                Acquaintance acq;
                if (!speaker.FindAcquaintanceExists(target, out acq) || acq == null)
                    return "they do not know that person well enough to have a view";

                var cap = ModConfig.MaxOpinionShiftPerLine.Value;

                // Loyalty resists. The better they know somebody, the less one conversation
                // moves the needle, so turning a close friend takes a real campaign.
                var resistance = Mathf.Lerp(1f, 1f - ModConfig.LoyaltyResistance.Value, Mathf.Clamp01(acq.known));
                var shift = Mathf.Clamp(delta, -cap, cap) * resistance;

                if (Mathf.Abs(shift) < 0.005f) return "they are too close to that person to be swayed by a sentence";

                var before = acq.like;
                acq.like = Mathf.Clamp01(acq.like + shift);
                if (Mathf.Abs(acq.like - before) < 0.001f) return "their view of that person is already at its limit";

                SessionLog.Note(speaker.GetCitizenName() + "'s view of " + target.GetCitizenName() +
                                " moved " + (shift > 0 ? "+" : "") + shift.ToString("0.00") + ".");
                return null;
            }
            catch (Exception e)
            {
                return "changing their view threw: " + e.Message;
            }
        }

        /// <summary>
        /// Take somebody else's side. Whoever is attacking the named person gets attacked back,
        /// and failing that the police are called off them - so the effect does something real
        /// in a fight and something real out of one, rather than only being available when
        /// there is already violence.
        /// </summary>
        public static string StandUpFor(Citizen speaker, string targetName, bool shouted)
        {
            if (!ModConfig.AllowThirdPartyOpinion.Value) return "taking somebody else's side is switched off";
            if (speaker == null || speaker.ai == null) return "no AI on this citizen";
            if (string.IsNullOrWhiteSpace(targetName)) return "no name given";

            Human target;
            var problem = Resolve(speaker, targetName, shouted, out target);
            if (problem != null) return problem;

            var targetCitizen = TryCitizen(target);

            // In a fight: go after whoever is going for them.
            try
            {
                if (targetCitizen != null && !speaker.ai.restrained && !speaker.ai.inCombat)
                {
                    foreach (var other in Earshot.CitizensWhoCanHear(speaker, shouted))
                    {
                        if (other == null || other.ai == null || !other.ai.inCombat) continue;
                        if (other.humanID == speaker.humanID) continue;

                        var victim = other.ai.attackTarget;
                        if (victim == null || victim.Pointer != target.Pointer) continue;

                        speaker.ai.SetInCombat(true);
                        speaker.ai.StartAttack(other);
                        SessionLog.Note(speaker.GetCitizenName() + " stepped in for " + target.GetCitizenName() + ".");
                        return null;
                    }
                }
            }
            catch { }

            // Out of a fight: vouch for them to the police.
            if (targetCitizen != null)
            {
                var officers = 0;
                foreach (var other in Earshot.CitizensWhoCanHear(speaker, shouted))
                {
                    try
                    {
                        if (other == null || other.ai == null || !other.isEnforcer) continue;
                        if (!other.ai.persuit) continue;
                        var chased = other.ai.persuitTarget;
                        if (chased == null || chased.Pointer != target.Pointer) continue;
                        other.ai.CancelPersue();
                        officers++;
                    }
                    catch { }
                }
                if (officers > 0)
                {
                    SessionLog.Note(speaker.GetCitizenName() + " called the police off " +
                                    target.GetCitizenName() + ".");
                    return null;
                }
            }

            return "nobody is threatening that person right now";
        }

        /// <summary>Who this citizen could credibly have an opinion about, for the prompt.</summary>
        public static List<string> KnownPeople(Citizen speaker, int max = 6)
        {
            var names = new List<string>();
            if (speaker == null) return names;

            try
            {
                if (speaker.acquaintances == null) return names;
                foreach (var acq in speaker.acquaintances)
                {
                    if (acq == null || acq.known < 0.3f) continue;
                    var other = acq.GetOther(speaker);
                    if (other == null || other.isPlayer) continue;

                    var name = other.GetCitizenName();
                    if (string.IsNullOrEmpty(name)) continue;

                    names.Add(name + " (" + Feeling(acq.like) + ")");
                    if (names.Count >= max) break;
                }
            }
            catch { }
            return names;
        }

        private static string Feeling(float like)
        {
            if (like < 0.2f) return "cannot stand them";
            if (like < 0.4f) return "wary of them";
            if (like < 0.6f) return "neutral";
            if (like < 0.8f) return "fond of them";
            return "very close";
        }

        /// <summary>
        /// Find who was meant: somebody in earshot first, then somebody they know. Refusing
        /// unknown names is what stops a model rearranging relationships between people it
        /// invented.
        /// </summary>
        private static string Resolve(Citizen speaker, string targetName, bool shouted, out Human target)
        {
            target = null;
            var wanted = targetName.Trim();

            try
            {
                foreach (var c in Earshot.CitizensWhoCanHear(speaker, shouted))
                {
                    if (c == null || c.isPlayer || c.humanID == speaker.humanID) continue;
                    if (Matches(c.GetCitizenName(), wanted)) { target = c; return null; }
                }

                if (speaker.acquaintances != null)
                {
                    foreach (var acq in speaker.acquaintances)
                    {
                        if (acq == null) continue;
                        var other = acq.GetOther(speaker);
                        if (other == null || other.isPlayer) continue;
                        if (Matches(other.GetCitizenName(), wanted)) { target = other; return null; }
                    }
                }
            }
            catch (Exception e)
            {
                return "looking that person up threw: " + e.Message;
            }

            return "they do not know anybody by that name";
        }

        private static bool Matches(string name, string wanted)
            => !string.IsNullOrEmpty(name) &&
               name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Citizen TryCitizen(Human human)
        {
            try { return human?.TryCast<Citizen>(); }
            catch { return null; }
        }
    }
}
