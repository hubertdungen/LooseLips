using System;
using System.Collections.Generic;
using LooseLips.Context;

namespace LooseLips.World
{
    /// <summary>
    /// Whether a particular kind of behaviour is even in this person's character.
    ///
    /// This exists because of a measured failure. Offered the full list of effects, a bartender
    /// asked for directions demanded payment three times out of three - not because the model
    /// was broken, but because "you may name a price" was sitting in front of it on a turn where
    /// no reasonable person would. Every attempt to fix that by rewording the prompt fixed one
    /// situation and unbalanced another.
    ///
    /// The fix is not more wording. It is that the mod already knows things the model has to
    /// guess: who this person is, what they are carrying, whether anyone is threatening them.
    /// So an effect that needs a reason is only offered when the reason exists. A greedy
    /// stranger can charge you for directions. A friendly one is never given the option, and
    /// therefore never takes it.
    ///
    /// Traits are matched by keyword because their names live in Unity assets rather than in the
    /// assembly, and an unrecognised trait must never silently disable behaviour - so where a
    /// judgement cannot be made, the answer is yes.
    /// </summary>
    public static class Disposition
    {
        private static readonly string[] Mercenary =
            { "greed", "money", "miser", "cheap", "gambl", "debt", "poor", "broke", "business", "corrupt" };

        private static readonly string[] Generous =
            { "generous", "kind", "helpful", "honest", "friendly", "charit", "loyal", "polite" };

        private static readonly string[] Timid =
            { "timid", "nervous", "cowar", "anxious", "meek", "frail", "shy" };

        private static readonly string[] Aggressive =
            { "aggress", "hot", "violent", "brave", "brawl", "temper", "bully", "fearless" };

        private static readonly string[] Talkative =
            { "gossip", "nosy", "chatty", "talkative", "loud", "curious" };

        private static bool Any(IEnumerable<string> traits, string[] keywords)
        {
            if (traits == null) return false;
            foreach (var trait in traits)
            {
                if (string.IsNullOrEmpty(trait)) continue;
                var lower = trait.ToLowerInvariant();
                foreach (var key in keywords)
                {
                    if (lower.Contains(key)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Would this person ask a stranger for money before helping them? Greed is one reason;
        /// so is simply not liking you enough to do anything for free.
        /// </summary>
        public static bool WouldHaggle(CitizenSnapshot s)
        {
            if (s == null) return true;
            if (Any(s.Traits, Mercenary)) return true;
            if (Any(s.Traits, Generous)) return false;

            // No opinion either way from their character, so let the relationship decide:
            // somebody who is wary of you wants something in return, a friend does not.
            return s.Like < 0.45f;
        }

        /// <summary>Is fighting back something this person would actually do?</summary>
        public static bool WouldFight(CitizenSnapshot s)
        {
            if (s == null) return true;
            if (s.IsEnforcer || s.CitizenIsArmed) return true;
            if (Any(s.Traits, Aggressive)) return true;
            return !Any(s.Traits, Timid);
        }

        /// <summary>Is running the more likely response than standing their ground?</summary>
        public static bool WouldFlee(CitizenSnapshot s)
        {
            if (s == null) return true;
            if (Any(s.Traits, Timid)) return true;
            return !s.IsEnforcer;
        }

        /// <summary>Do they have anybody to gossip about, and the inclination?</summary>
        public static bool WouldTalkAboutOthers(CitizenSnapshot s)
        {
            if (s == null) return true;
            if (s.Opinions == null || s.Opinions.Count == 0) return false;   // nobody to discuss
            if (Any(s.Traits, Talkative)) return true;
            return !Any(s.Traits, Generous) || s.Like > 0.6f;
        }

        /// <summary>A short note for the prompt, so the model knows why it has these choices.</summary>
        public static string Describe(CitizenSnapshot s)
        {
            if (s == null) return null;

            var notes = new List<string>();
            if (Any(s.Traits, Mercenary)) notes.Add("you do not do favours for nothing");
            if (Any(s.Traits, Generous)) notes.Add("you help people without being asked twice");
            if (Any(s.Traits, Timid)) notes.Add("you frighten easily");
            if (Any(s.Traits, Aggressive)) notes.Add("you do not back down");
            if (Any(s.Traits, Talkative)) notes.Add("you enjoy talking about other people");

            return notes.Count == 0 ? null : "In character: " + string.Join(", ", notes) + ".";
        }
    }
}
