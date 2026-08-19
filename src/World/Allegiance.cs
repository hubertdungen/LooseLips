using System;
using System.Collections.Generic;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Where a citizen stands with you, and what that makes them do when it matters.
    ///
    /// The game has one number for this - <c>Acquaintance.like</c> - and it is a feeling, not a
    /// commitment. Somebody can like you and still watch you get jumped. Taking a side is a
    /// separate thing, so it is tracked separately: liking is the ground a decision is made on,
    /// and siding with you is the decision.
    ///
    /// It is deliberately hard to hold. An ally who is frightened badly enough backs out, and
    /// anybody can be turned against you by what you say next. Nothing here is permanent, which
    /// is the point - a friend you have to keep is worth more than a flag you set once.
    /// </summary>
    public static class Allegiance
    {
        public enum Stance
        {
            Hostile,
            Wary,
            Neutral,
            Friendly,
            Ally
        }

        /// <summary>Explicit stances taken in conversation, over and above how much they like you.</summary>
        private static readonly Dictionary<int, Stance> Declared = new Dictionary<int, Stance>();

        public static Stance Of(Citizen citizen)
        {
            if (citizen == null) return Stance.Neutral;

            Stance declared;
            if (Declared.TryGetValue(citizen.humanID, out declared)) return declared;

            // Nothing declared, so fall back to how they feel about you.
            try
            {
                var player = Player.Instance;
                if (player == null) return Stance.Neutral;

                Acquaintance acq;
                if (!citizen.FindAcquaintanceExists(player, out acq) || acq == null) return Stance.Neutral;

                if (acq.like < 0.2f) return Stance.Hostile;
                if (acq.like < 0.4f) return Stance.Wary;
                if (acq.like > 0.75f) return Stance.Friendly;
            }
            catch { }

            return Stance.Neutral;
        }

        public static string Describe(Citizen citizen)
        {
            switch (Of(citizen))
            {
                case Stance.Hostile: return "You have decided you are against this investigator.";
                case Stance.Wary: return "You do not trust this investigator.";
                case Stance.Friendly: return "You are on good terms with this investigator.";
                case Stance.Ally: return "You have taken this investigator's side, and will back them up.";
                default: return null;
            }
        }

        /// <summary>Take the player's side. This is what turns a follower into a bodyguard.</summary>
        public static string SideWith(Citizen citizen)
        {
            if (!ModConfig.AllowAllegiance.Value) return "taking sides is switched off";
            if (citizen == null) return "nobody to take a side";

            try
            {
                // Somebody who barely tolerates you does not sign up. The relationship has to be
                // built first, which is what the rest of the mod is for.
                var player = Player.Instance;
                Acquaintance acq;
                if (player != null && citizen.FindAcquaintanceExists(player, out acq) && acq != null)
                {
                    if (acq.like < ModConfig.AllyLikeThreshold.Value)
                        return "they do not like you nearly enough for that";
                }
                else
                {
                    return "you are still a stranger to them";
                }
            }
            catch (Exception e)
            {
                return "checking how they feel threw: " + e.Message;
            }

            Declared[citizen.humanID] = Stance.Ally;
            return null;
        }

        /// <summary>Turn against the player.</summary>
        public static string TurnAgainst(Citizen citizen)
        {
            if (!ModConfig.AllowAllegiance.Value) return "taking sides is switched off";
            if (citizen == null) return "nobody to turn";

            Declared[citizen.humanID] = Stance.Hostile;
            FollowDirector.Stop(citizen);   // an enemy does not tag along
            return null;
        }

        /// <summary>Drop back to whatever their feelings alone would say.</summary>
        public static void ClearDeclared(Citizen citizen)
        {
            if (citizen != null) Declared.Remove(citizen.humanID);
        }

        public static void Clear() => Declared.Clear();

        public static bool IsAlly(Citizen citizen) => Of(citizen) == Stance.Ally;

        /// <summary>
        /// Allies stepping in. Anyone in earshot whose AI is currently attacking the player gets
        /// attacked back by every ally close enough to see it.
        ///
        /// Fear wins over loyalty here: an ally who is already panicking will not wade in, which
        /// keeps a declared ally from being an invincible switch.
        /// </summary>
        public static void DefendPlayer()
        {
            if (!ModConfig.AllowAllegiance.Value || !ModConfig.AlliesDefendYou.Value) return;

            var player = Player.Instance;
            if (player == null) return;

            List<Citizen> nearby;
            try { nearby = Earshot.CitizensWhoCanHear(player, true); }
            catch { return; }

            // Who is going for the player right now?
            Citizen aggressor = null;
            foreach (var c in nearby)
            {
                try
                {
                    if (c == null || c.ai == null || !c.ai.inCombat) continue;
                    var target = c.ai.attackTarget;
                    if (target == null) continue;
                    if (target.Pointer != player.Pointer) continue;
                    aggressor = c;
                    break;
                }
                catch { }
            }

            if (aggressor == null) return;

            foreach (var ally in nearby)
            {
                try
                {
                    if (ally == null || ally.ai == null) continue;
                    if (ally.humanID == aggressor.humanID) continue;
                    if (!IsAlly(ally)) continue;
                    if (ally.ai.restrained || ally.ai.inCombat) continue;
                    if (ally.ai.alertness > ModConfig.AllyNerveThreshold.Value) continue;  // too scared

                    ally.ai.SetInCombat(true);
                    ally.ai.StartAttack(aggressor);
                    SessionLog.Note(ally.GetCitizenName() + " stepped in against " +
                                    aggressor.GetCitizenName() + ".");
                }
                catch { }
            }
        }
    }
}
