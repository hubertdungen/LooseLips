using System;
using System.Collections.Generic;
using LooseLips.Core;
using LooseLips.World;
using UnityEngine;

namespace LooseLips.Context
{
    /// <summary>
    /// Reads live game state into a <see cref="CitizenSnapshot"/>.
    /// Must run on the Unity main thread. Every lookup is defensive: a missing sub-object
    /// costs one field, never the whole snapshot.
    /// </summary>
    public static class ContextBuilder
    {
        public static CitizenSnapshot Build(Citizen citizen, bool shouted, string vanillaLine)
        {
            var s = new CitizenSnapshot
            {
                WasShouted = shouted,
                VanillaLine = vanillaLine
            };

            if (citizen == null) return s;

            var player = Player.Instance;

            Try(() =>
            {
                s.CitizenId = citizen.humanID;
                s.FullName = citizen.GetCitizenName();
                s.CasualName = citizen.GetCasualName();
                s.Age = citizen.GetAge();
            });

            Try(() =>
            {
                if (citizen.job != null)
                {
                    s.Job = citizen.job.name;
                    if (citizen.job.employer != null) s.Employer = citizen.job.employer.name;
                }
            });

            Try(() => { if (citizen.home != null) s.HomeAddress = citizen.home.name; });

            Try(() =>
            {
                if (citizen.characterTraits == null) return;
                foreach (var t in citizen.characterTraits)
                {
                    if (t == null) continue;
                    var n = !string.IsNullOrEmpty(t.name) ? t.name
                          : (t.trait != null ? t.trait.name : null);
                    if (!string.IsNullOrEmpty(n)) s.Traits.Add(n);
                }
            });

            // --- Relationship with the player ---
            Try(() =>
            {
                Acquaintance acq;
                if (player != null && citizen.FindAcquaintanceExists(player, out acq) && acq != null)
                {
                    s.HasMetPlayer = true;
                    s.Known = acq.known;
                    s.Like = acq.like;
                    if (acq.connections != null)
                    {
                        foreach (var c in acq.connections) s.ConnectionsToPlayer.Add(c.ToString());
                    }
                }
                else
                {
                    // Strangers still have a baseline disposition.
                    s.Known = 0f;
                    s.Like = 0.5f;
                }
            });

            // --- Situation ---
            Try(() => { if (SessionData.Instance != null) s.TimeOfDay = SessionData.Instance.TimeAndDate(SessionData.Instance.gameTime, true, true, true); });
            Try(() => { if (citizen.currentGameLocation != null) s.LocationName = citizen.currentGameLocation.name; });
            Try(() => { if (citizen.currentRoom != null) s.RoomName = citizen.currentRoom.name; });

            Try(() =>
            {
                s.AtHome = citizen.isHome;
                s.AtWork = citizen.isAtWork;
                s.IsEnforcer = citizen.isEnforcer;
                s.IsOnDuty = citizen.isOnDuty;
            });

            Try(() =>
            {
                if (citizen.ai == null) return;
                s.InCombat = citizen.ai.inCombat;
                s.IsFleeing = citizen.ai.inFleeState;
                s.IsRestrained = citizen.ai.restrained;
                s.Alertness = Mathf.Clamp01(citizen.ai.alertness);
            });

            Try(() =>
            {
                s.CitizenHeldItem = DescribeHeld(citizen);
                s.CitizenIsArmed = !string.IsNullOrEmpty(s.CitizenHeldItem);
            });

            Try(() =>
            {
                if (player == null) return;
                s.PlayerIsTrespassing = player.isTrespassing;
                s.PlayerHeldItem = DescribeHeld(player);
                s.PlayerIsArmed = !string.IsNullOrEmpty(s.PlayerHeldItem);
            });

            Try(() =>
            {
                foreach (var other in Earshot.CitizensWhoCanHear(citizen, shouted))
                {
                    if (other == null || other.humanID == citizen.humanID) continue;
                    var n = other.GetCasualName();
                    if (!string.IsNullOrEmpty(n)) s.Bystanders.Add(n);
                    if (s.Bystanders.Count >= 8) break;
                }
            });

            Try(() => GroundTruthReader.Fill(citizen, s));

            Try(() => s.PermittedEffects.AddRange(WorldEffectExecutor.PermittedEffectNames()));
            Try(() => s.CanTestifyAbout.AddRange(Testimony.PossibleSubjects(citizen)));
            Try(() => s.Carrying.AddRange(WalletReader.Describe(citizen)));
            Try(() => s.PriorConversations = ConversationMemory.TurnsWith(citizen.humanID));
            Try(() => s.IsFollowingPlayer = FollowDirector.IsFollowing(citizen));
            Try(() => s.AllegianceNote = Allegiance.Describe(citizen));
            Try(() => s.PendingDemand = Negotiation.PendingFor(citizen));
            Try(() => s.Opinions.AddRange(Opinion.KnownPeople(citizen)));

            return s;
        }

        /// <summary>Name of whatever the actor is holding, or null when empty-handed.</summary>
        private static string DescribeHeld(Actor actor)
        {
            if (actor == null) return null;
            var held = actor.rightHandInteractable ?? actor.leftHandInteractable;
            if (held == null) return null;
            try
            {
                return held.preset != null ? held.preset.name : held.name;
            }
            catch
            {
                return null;
            }
        }

        private static void Try(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                {
                    Plugin.Log.LogWarning("Snapshot field failed: " + e.Message);
                }
            }
        }
    }
}
