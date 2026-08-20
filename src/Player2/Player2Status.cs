using System;
using UnityEngine;

namespace LooseLips.Player2
{
    /// <summary>
    /// What the Player2 app is currently telling us, in terms a player can act on.
    ///
    /// This exists because of a wrong assumption worth recording. An early measurement showed a
    /// chat request costing zero credits, and the mod was built on the idea that generation was
    /// effectively free and only the wait mattered. Measured properly over several requests, the
    /// balance does move - roughly a third of a credit per exchange - and it refills over time.
    /// On a well stocked account that is invisible. On a free one it is the whole story, and a
    /// mod that spends somebody's balance on ambient chatter they did not ask for is a bad
    /// guest on their machine.
    ///
    /// So the balance is watched, the three failure modes that actually happen are told apart -
    /// not signed in, out of credits, going too fast - and each one gets a different answer
    /// rather than a shrug in the log.
    /// </summary>
    public static class Player2Status
    {
        public enum State
        {
            Unknown,
            Fine,
            NotSignedIn,
            OutOfCredits,
            RateLimited,
            Unreachable
        }

        public static State Current { get; private set; } = State.Unknown;

        /// <summary>Credits remaining, or -1 when unknown.</summary>
        public static int Joules { get; private set; } = -1;

        public static string Tier { get; private set; } = "";

        /// <summary>While this is in the future, only the player's own conversations are allowed.</summary>
        public static float QuietUntil { get; private set; }

        public static bool ShouldHoldBackAmbient
            => Current == State.OutOfCredits
            || Current == State.RateLimited
            || Current == State.NotSignedIn
            || Time.time < QuietUntil
            || (Joules >= 0 && Joules < Core.ModConfig.MinJoulesForAmbient.Value);

        public static void Reading(int joules, string tier)
        {
            Joules = joules;
            Tier = tier ?? "";
            if (Current == State.Unknown || Current == State.Unreachable) Current = State.Fine;
        }

        /// <summary>
        /// Interpret an HTTP status. The three that matter each mean something different to the
        /// person playing, and none of them is a bug in the mod.
        /// </summary>
        public static void Saw(int statusCode)
        {
            switch (statusCode)
            {
                case 401:
                    Set(State.NotSignedIn, 300f,
                        "Player2 says you are not signed in. Open the Player2 app and log in.");
                    break;

                case 402:
                    Set(State.OutOfCredits, 600f,
                        "Player2 is out of credits. Free-form dialogue will pause until the balance recovers; " +
                        "background chatter stays off in the meantime.");
                    break;

                case 429:
                    // Backing off further each time rather than hammering a server that has
                    // already said no.
                    var wait = Mathf.Min(60f * (1 + Consecutive429), 600f);
                    Consecutive429++;
                    Set(State.RateLimited, wait,
                        "Player2 is rate limiting us. Holding back for " + Mathf.RoundToInt(wait) + " s.");
                    break;

                default:
                    if (statusCode >= 200 && statusCode < 300)
                    {
                        Consecutive429 = 0;
                        if (Current != State.Fine)
                        {
                            Current = State.Fine;
                            Plugin.Log.LogInfo("Player2 is answering normally again.");
                        }
                    }
                    break;
            }
        }

        public static void Unreachable()
        {
            Current = State.Unreachable;
        }

        private static int Consecutive429;

        private static void Set(State state, float quietSeconds, string message)
        {
            var changed = Current != state;
            Current = state;
            QuietUntil = Time.time + quietSeconds;
            if (changed) Plugin.Log.LogWarning(message);
        }

        public static string Describe()
        {
            var balance = Joules >= 0
                ? Joules + " credits" + (string.IsNullOrEmpty(Tier) ? "" : " on " + Tier)
                : "credits unknown";

            switch (Current)
            {
                case State.NotSignedIn: return "Not signed in to Player2.";
                case State.OutOfCredits: return "Out of Player2 credits. " + balance;
                case State.RateLimited: return "Rate limited by Player2. " + balance;
                case State.Unreachable: return "Cannot reach the Player2 app.";
                case State.Fine: return balance;
                default: return "Not checked yet.";
            }
        }

        public static void Reset()
        {
            Current = State.Unknown;
            Joules = -1;
            Tier = "";
            QuietUntil = 0f;
            Consecutive429 = 0;
        }
    }
}
