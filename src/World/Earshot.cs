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

        public static float Radius(VoiceLevel level) => Voice.RadiusOf(level);

        /// <summary>
        /// Who can hear a line at a given volume. A whisper reaches barely past the person it
        /// was meant for; only a shout gets through into the next room.
        /// </summary>
        public static List<Citizen> CitizensWhoCanHear(Actor origin, VoiceLevel level)
            => Gather(origin, Voice.RadiusOf(level), Voice.CarriesNextDoor(level));

        /// <summary>
        /// Citizens within earshot of <paramref name="origin"/>. Excludes the player, the
        /// dead, and anyone asleep or unconscious.
        /// </summary>
        public static List<Citizen> CitizensWhoCanHear(Actor origin, bool shouted)
            => Cached(origin, Radius(shouted), shouted);

        /// <summary>
        /// A very short-lived cache in front of the sweep.
        ///
        /// Working out who can hear something means walking rooms, their occupants and a
        /// distance check each - three allocations and a lot of pointer chasing. Several parts
        /// of the mod want that answer in the same instant, and one of them was asking sixty
        /// times a second, so the same walk was being repeated for an answer that cannot
        /// meaningfully change between frames. People do not move far in a tenth of a second.
        ///
        /// Keyed on the origin and radius, because "who can hear a whisper from here" and "who
        /// can hear a shout from here" are different questions.
        /// </summary>
        private static readonly Dictionary<long, CachedSweep> Recent = new Dictionary<long, CachedSweep>();

        private sealed class CachedSweep
        {
            public float TakenAt;
            public List<Citizen> Result;
        }

        private const float CacheSeconds = 0.1f;

        private static List<Citizen> Cached(Actor origin, float radius, bool shouted)
        {
            if (origin == null) return new List<Citizen>();

            long key;
            try { key = ((long)origin.GetInstanceID() << 20) ^ (long)(radius * 100f); }
            catch { return Gather(origin, radius, shouted); }

            CachedSweep sweep;
            if (Recent.TryGetValue(key, out sweep) && Time.time - sweep.TakenAt < CacheSeconds)
                return sweep.Result;

            var fresh = Gather(origin, radius, shouted);
            Recent[key] = new CachedSweep { TakenAt = Time.time, Result = fresh };

            // The dictionary is keyed by actor, so it would otherwise grow with every citizen
            // the mod ever asked about.
            if (Recent.Count > 32) Prune();
            return fresh;
        }

        private static void Prune()
        {
            var stale = new List<long>();
            foreach (var pair in Recent)
            {
                if (Time.time - pair.Value.TakenAt >= CacheSeconds) stale.Add(pair.Key);
            }
            foreach (var key in stale) Recent.Remove(key);
        }

        public static void ClearCache() => Recent.Clear();

        private static List<Citizen> Gather(Actor origin, float radius, bool shouted)
        {
            var result = new List<Citizen>();
            if (origin == null) return result;

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
