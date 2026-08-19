using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.World
{
    /// <summary>
    /// Works out who can hear a line. Rooms come first because walls matter more than
    /// raw distance in this game, then distance trims the result down to the configured
    /// reach. A shout also carries into adjacent rooms.
    /// </summary>
    public static class Earshot
    {
        /// <summary>Radius in metres for the current mode.</summary>
        public static float Radius(bool shouted)
            => shouted ? ModConfig.ShoutRadius.Value : ModConfig.TalkRadius.Value;

        /// <summary>
        /// Citizens within earshot of <paramref name="origin"/>. Excludes the player, the
        /// dead, and anyone asleep or unconscious.
        /// </summary>
        public static List<Citizen> CitizensWhoCanHear(Actor origin, bool shouted)
        {
            var result = new List<Citizen>();
            if (origin == null) return result;

            var radius = Radius(shouted);
            var originPos = origin.transform != null ? origin.transform.position : Vector3.zero;

            var rooms = new HashSet<NewRoom>();
            try
            {
                if (origin.currentRoom != null)
                {
                    rooms.Add(origin.currentRoom);
                    if (shouted && origin.currentRoom.adjacentRooms != null)
                    {
                        foreach (var adj in origin.currentRoom.adjacentRooms)
                        {
                            if (adj != null) rooms.Add(adj);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value) Plugin.Log.LogWarning("Room walk failed: " + e.Message);
            }

            var seen = new HashSet<int>();

            foreach (var room in rooms)
            {
                Il2CppSystem.Collections.Generic.HashSet<Actor> occupants = null;
                try { occupants = room.currentOccupants; } catch { }
                if (occupants == null) continue;

                foreach (var actor in occupants)
                {
                    var cit = TryAsCitizen(actor);
                    if (cit == null) continue;
                    if (!CanHear(cit)) continue;
                    if (!seen.Add(cit.humanID)) continue;

                    if (WithinRange(cit, originPos, radius)) result.Add(cit);
                }
            }

            // On the street there is no meaningful room, so fall back to a plain radius
            // sweep over the city directory.
            if (rooms.Count == 0)
            {
                try
                {
                    var directory = CityData.Instance != null ? CityData.Instance.citizenDirectory : null;
                    if (directory != null)
                    {
                        foreach (var cit in directory)
                        {
                            if (cit == null || !CanHear(cit)) continue;
                            if (!seen.Add(cit.humanID)) continue;
                            if (WithinRange(cit, originPos, radius)) result.Add(cit);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (ModConfig.VerboseLogging.Value) Plugin.Log.LogWarning("Directory sweep failed: " + e.Message);
                }
            }

            return result;
        }

        private static bool WithinRange(Actor actor, Vector3 originPos, float radius)
        {
            try
            {
                if (actor.transform == null) return false;
                return Vector3.Distance(actor.transform.position, originPos) <= radius;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanHear(Citizen cit)
        {
            try
            {
                if (cit.isPlayer || cit.isDead || cit.isAsleep || cit.isStunned) return false;
                return cit.canListen;
            }
            catch
            {
                return false;
            }
        }

        private static Citizen TryAsCitizen(Actor actor)
        {
            if (actor == null) return null;
            try
            {
                return actor.TryCast<Citizen>();
            }
            catch
            {
                return null;
            }
        }
    }
}
