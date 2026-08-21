using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LooseLips.Core
{
    /// <summary>
    /// What your conversations achieved, kept between sessions.
    ///
    /// This closes the worst inconsistency in the mod. Conversations already persisted, so a
    /// citizen would greet you remembering, word for word, that they had sworn to back you up -
    /// and not be your ally, because that part lived only in memory. A contradiction you can
    /// see is worse than having no memory at all: forgetting is forgivable, remembering the
    /// promise and not the commitment is not.
    ///
    /// Kept deliberately small. Allegiances and unsettled prices are decisions that ought to
    /// outlive a session. Followers are not: somebody trailing you through a save and a quit is
    /// stranger than them having wandered off, and the arrangement runs on a timer anyway.
    /// </summary>
    public static class WorldMemory
    {
        public sealed class Record
        {
            /// <summary>Citizen id to stance, as the name of the enum value.</summary>
            [JsonPropertyName("allegiance")]
            public Dictionary<string, string> Allegiance { get; set; } = new Dictionary<string, string>();

            [JsonPropertyName("demands")]
            public Dictionary<string, Owed> Demands { get; set; } = new Dictionary<string, Owed>();
        }

        public sealed class Owed
        {
            [JsonPropertyName("amount")] public int Amount { get; set; }
            [JsonPropertyName("for")] public string For { get; set; }
        }

        private static string _loadedSeed;

        private static string CurrentSeed()
        {
            try
            {
                var city = CityData.Instance;
                if (city == null) return null;
                var seed = city.seed;
                if (string.IsNullOrWhiteSpace(seed)) return null;

                var clean = "";
                foreach (var c in seed)
                {
                    if (char.IsLetterOrDigit(c) || c == '-' || c == '_') clean += c;
                }
                return clean.Length > 0 ? clean : null;
            }
            catch
            {
                return null;
            }
        }

        private static string PathFor(string seed)
        {
            try
            {
                var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "LooseLips-memories");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, seed + ".world.json");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load this city's standing decisions once the city exists. Safe to call every frame:
        /// it does nothing until a seed appears, and nothing again after that.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (!ModConfig.RememberBetweenSessions.Value) return;

            var seed = CurrentSeed();
            if (seed == null || seed == _loadedSeed) return;
            _loadedSeed = seed;

            var path = PathFor(seed);
            if (path == null || !File.Exists(path)) return;

            try
            {
                var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path));
                if (record == null) return;

                World.Allegiance.Restore(record.Allegiance);
                World.Negotiation.Restore(record.Demands);

                var allies = record.Allegiance != null ? record.Allegiance.Count : 0;
                var owed = record.Demands != null ? record.Demands.Count : 0;
                if (allies > 0 || owed > 0)
                    Plugin.Log.LogInfo("Picked up where you left off: " + allies + " people had taken a side, " +
                                       owed + " were waiting to be paid.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read this city's standing decisions: " + e.Message);
            }
        }

        public static void Save()
        {
            if (!ModConfig.RememberBetweenSessions.Value) return;

            var seed = _loadedSeed ?? CurrentSeed();
            if (seed == null) return;

            var path = PathFor(seed);
            if (path == null) return;

            try
            {
                var record = new Record
                {
                    Allegiance = World.Allegiance.Export(),
                    Demands = World.Negotiation.Export()
                };
                File.WriteAllText(path, JsonSerializer.Serialize(record));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not save this city's standing decisions: " + e.Message);
            }
        }

        public static void Clear() => _loadedSeed = null;
    }
}
