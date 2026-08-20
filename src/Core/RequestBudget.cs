using System;
using System.Collections.Generic;
using UnityEngine;

namespace LooseLips.Core
{
    /// <summary>
    /// Decides what is allowed to ask the model, and refuses the rest.
    ///
    /// Two things are scarce, and an early measurement got one of them wrong. A single request
    /// appeared to cost no credits at all, which was rounding: measured across several, an
    /// exchange of roughly 750 tokens costs about a third of a credit, and the balance refills
    /// over time. On a well stocked account that is invisible; on a free one it is the whole
    /// story, so the ceiling here is never assumed - it is read from the account.
    ///
    /// The other scarce thing is time. Every line is seconds of waiting, and a street where
    /// eight people each react to a gunshot would queue the better part of a minute and arrive
    /// after the moment had passed.
    ///
    /// So ambient life is rationed on both at once - one generation at a time, a floor between
    /// lines, a per-person cooldown, an hourly ceiling, and a reserve of credits it will not
    /// dip into - while anything the player is directly part of is never rationed at all. A
    /// conversation you started must always answer.
    /// </summary>
    public static class RequestBudget
    {
        public enum Kind
        {
            /// <summary>The player is waiting for this. Never refused.</summary>
            PlayerConversation,

            /// <summary>Two citizens talking to each other.</summary>
            Overheard,

            /// <summary>A citizen reacting to something that just happened.</summary>
            Ambient
        }

        private static int _ambientInFlight;
        private static float _lastAmbient;
        private static readonly Queue<float> RecentHour = new Queue<float>();
        private static readonly Dictionary<int, float> PerCitizen = new Dictionary<int, float>();

        // Counters shown in the settings window, so the rationing is visible rather than mysterious.
        public static int SpentThisHour => RecentHour.Count;
        public static int TotalRequests { get; private set; }
        public static int RefusedByBudget { get; private set; }
        public static string LastRefusal { get; private set; } = "";

        /// <summary>
        /// Ask permission. Returns true when the request may go ahead, and reserves a slot
        /// that must be released with <see cref="Finished"/>.
        /// </summary>
        public static bool TryTake(Kind kind, Citizen who = null)
        {
            // The player is never made to wait on a budget.
            if (kind == Kind.PlayerConversation)
            {
                TotalRequests++;
                return true;
            }

            if (!ModConfig.EnableAmbientLife.Value) return Refuse("ambient life is switched off");

            // Whatever the local limits say, the account has the final word. On a free plan this
            // is the setting that matters: what is left gets saved for conversations the player
            // actually started.
            if (Player2.Player2Status.ShouldHoldBackAmbient)
                return Refuse(Player2.Player2Status.Describe());

            Trim();

            if (RecentHour.Count >= ModConfig.MaxAmbientPerHour.Value)
                return Refuse("hourly ceiling reached (" + ModConfig.MaxAmbientPerHour.Value + ")");

            if (_ambientInFlight >= 1)
                return Refuse("one is already being generated");

            if (Time.time - _lastAmbient < ModConfig.MinSecondsBetweenAmbient.Value)
                return Refuse("too soon after the last one");

            if (who != null)
            {
                float last;
                if (PerCitizen.TryGetValue(who.humanID, out last) &&
                    Time.time - last < ModConfig.PerCitizenCooldown.Value)
                    return Refuse("that person spoke too recently");
            }

            _ambientInFlight++;
            _lastAmbient = Time.time;
            RecentHour.Enqueue(Time.time);
            if (who != null) PerCitizen[who.humanID] = Time.time;

            TotalRequests++;
            return true;
        }

        /// <summary>Release the slot taken by <see cref="TryTake"/>.</summary>
        public static void Finished(Kind kind)
        {
            if (kind == Kind.PlayerConversation) return;
            if (_ambientInFlight > 0) _ambientInFlight--;
        }

        private static bool Refuse(string why)
        {
            RefusedByBudget++;
            LastRefusal = why;
            return false;
        }

        /// <summary>Drop anything older than an hour of play from the running count.</summary>
        private static void Trim()
        {
            var cutoff = Time.time - 3600f;
            while (RecentHour.Count > 0 && RecentHour.Peek() < cutoff) RecentHour.Dequeue();
        }

        public static void Reset()
        {
            _ambientInFlight = 0;
            _lastAmbient = 0f;
            RecentHour.Clear();
            PerCitizen.Clear();
            TotalRequests = 0;
            RefusedByBudget = 0;
            LastRefusal = "";
        }

        public static string Summary()
        {
            Trim();
            return RecentHour.Count + " of " + ModConfig.MaxAmbientPerHour.Value + " this hour, " +
                   TotalRequests + " requests all session, " + RefusedByBudget + " held back" +
                   (string.IsNullOrEmpty(LastRefusal) ? "" : " (last: " + LastRefusal + ")");
        }
    }
}
