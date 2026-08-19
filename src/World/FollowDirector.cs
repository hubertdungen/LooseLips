using System;
using System.Collections.Generic;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Talking somebody into coming with you.
    ///
    /// Be clear about what this is: Shadows of Doubt has no follow behaviour and no companion
    /// concept anywhere in its AI. There is no preset to borrow and no target to set. What
    /// exists is <c>Investigate</c>, which sends a citizen to a point and lets them lose
    /// interest. So following is built by re-pointing that at wherever you are standing, every
    /// few seconds, for as long as the arrangement lasts.
    ///
    /// The result behaves like somebody trailing you rather than a party member glued to your
    /// shoulder - they lag, they take their own route, and they give up if you outrun them.
    /// That is a fair reflection of what the game can actually support, and it is honest about
    /// its limits: it ends on a timer, when they lose sight of you for too long, or when
    /// anything more urgent takes over their AI.
    /// </summary>
    public static class FollowDirector
    {
        private sealed class Follower
        {
            public Citizen Citizen;
            public float Until;
            public float NextNudge;
        }

        private static readonly Dictionary<int, Follower> Following = new Dictionary<int, Follower>();

        public static int Count => Following.Count;

        public static bool IsFollowing(Citizen c)
            => c != null && Following.ContainsKey(c.humanID);

        /// <summary>Names of everyone currently tagging along, for the settings window.</summary>
        public static List<string> Names()
        {
            var names = new List<string>();
            foreach (var f in Following.Values)
            {
                try { names.Add(f.Citizen.GetCitizenName()); } catch { }
            }
            return names;
        }

        public static string Start(Citizen citizen)
        {
            if (!ModConfig.AllowFollowing.Value) return "getting people to follow you is switched off";
            if (citizen == null || citizen.ai == null) return "no AI on this citizen";
            if (citizen.ai.restrained) return "they are restrained";

            if (Following.Count >= ModConfig.MaxFollowers.Value && !IsFollowing(citizen))
                return "you already have as many people with you as the mod allows";

            Following[citizen.humanID] = new Follower
            {
                Citizen = citizen,
                Until = Time.time + ModConfig.FollowDuration.Value,
                NextNudge = 0f
            };
            return null;
        }

        public static string Stop(Citizen citizen)
        {
            if (citizen == null) return "nobody to stop";
            if (!Following.Remove(citizen.humanID)) return "they were not following you";
            return null;
        }

        public static void StopAll() => Following.Clear();

        /// <summary>
        /// Keep pointing the followers at the player. Called once a frame; the actual nudge
        /// happens on its own slower schedule, because re-issuing a goal every frame would
        /// stop them ever arriving anywhere.
        /// </summary>
        public static void Tick()
        {
            if (Following.Count == 0) return;

            var player = Player.Instance;
            if (player == null)
            {
                Following.Clear();
                return;
            }

            List<int> expired = null;

            foreach (var pair in Following)
            {
                var follower = pair.Value;
                var citizen = follower.Citizen;

                var done = false;
                try
                {
                    if (citizen == null || citizen.isDead || citizen.ai == null) done = true;
                    else if (Time.time > follower.Until) done = true;
                    else if (citizen.ai.restrained) done = true;
                    else if (Vector3.Distance(citizen.transform.position, player.transform.position) >
                             ModConfig.FollowGiveUpDistance.Value) done = true;
                }
                catch
                {
                    done = true;
                }

                if (done)
                {
                    (expired ??= new List<int>()).Add(pair.Key);
                    continue;
                }

                if (Time.time < follower.NextNudge) continue;
                follower.NextNudge = Time.time + ModConfig.FollowNudgeInterval.Value;

                try
                {
                    var node = player.currentNode;
                    if (node == null) continue;

                    // Walking, not running: somebody who agreed to come along is not alarmed.
                    citizen.ai.SetInvestigationUrgency(NewAIController.InvestigationUrgency.walk);
                    citizen.ai.Investigate(node, player.transform.position, null,
                        NewAIController.ReactionState.none, 1f, 0, false);
                }
                catch (Exception e)
                {
                    if (ModConfig.VerboseLogging.Value)
                        Plugin.Log.LogWarning("Could not nudge a follower: " + e.Message);
                    (expired ??= new List<int>()).Add(pair.Key);
                }
            }

            if (expired == null) return;
            foreach (var id in expired)
            {
                Follower gone;
                if (Following.TryGetValue(id, out gone))
                {
                    try { SessionLog.Note(gone.Citizen.GetCitizenName() + " stopped following you."); }
                    catch { }
                }
                Following.Remove(id);
            }
        }
    }
}
