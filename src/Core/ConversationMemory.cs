using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LooseLips.Player2;

namespace LooseLips.Core
{
    /// <summary>
    /// What each citizen remembers you saying to them, and what they said back.
    ///
    /// This used to be deliberately session-only, on the reasoning that a remembered
    /// conversation could contradict a world the save had since moved on from. That
    /// reasoning was wrong in the way that matters: a citizen who greets you as a stranger
    /// the day after you talked them into handing over their door code is a worse break
    /// than any stale line could be. Being remembered is most of what makes talking to
    /// somebody feel like talking to somebody.
    ///
    /// Memories are keyed by the city seed as well as the citizen, so two different cities
    /// can never inherit each other's conversations, and each citizen keeps only a bounded
    /// number of turns.
    /// </summary>
    public static class ConversationMemory
    {
        private static readonly Dictionary<int, List<ChatMessage>> Threads = new Dictionary<int, List<ChatMessage>>();
        private static string _loadedSeed;

        public static IReadOnlyList<ChatMessage> Get(int citizenId)
        {
            List<ChatMessage> thread;
            return Threads.TryGetValue(citizenId, out thread) ? thread : new List<ChatMessage>();
        }

        /// <summary>True when this citizen has spoken with the player before.</summary>
        public static bool HasHistory(int citizenId)
        {
            List<ChatMessage> thread;
            return Threads.TryGetValue(citizenId, out thread) && thread.Count > 0;
        }

        public static int TurnsWith(int citizenId)
        {
            List<ChatMessage> thread;
            return Threads.TryGetValue(citizenId, out thread) ? thread.Count / 2 : 0;
        }

        public static void Record(int citizenId, string playerLine, string citizenLine)
        {
            List<ChatMessage> thread;
            if (!Threads.TryGetValue(citizenId, out thread))
            {
                thread = new List<ChatMessage>();
                Threads[citizenId] = thread;
            }

            if (!string.IsNullOrWhiteSpace(playerLine)) thread.Add(ChatMessage.User(playerLine));
            if (!string.IsNullOrWhiteSpace(citizenLine)) thread.Add(ChatMessage.Assistant(citizenLine));

            // Two entries per exchange, so the cap is expressed in turns.
            var max = ModConfig.HistoryTurnsPerCitizen.Value * 2;
            if (max <= 0)
            {
                thread.Clear();
                return;
            }
            while (thread.Count > max) thread.RemoveAt(0);

            if (ModConfig.RememberBetweenSessions.Value) Save();
        }

        public static void Forget(int citizenId)
        {
            Threads.Remove(citizenId);
            if (ModConfig.RememberBetweenSessions.Value) Save();
        }

        public static void Clear()
        {
            Threads.Clear();
            _loadedSeed = null;
        }

        // --- Persistence --------------------------------------------------------

        private static string CurrentSeed()
        {
            try
            {
                var city = CityData.Instance;
                if (city == null) return null;
                var seed = city.seed;
                return string.IsNullOrWhiteSpace(seed) ? null : Sanitise(seed);
            }
            catch
            {
                return null;
            }
        }

        private static string PathForSeed(string seed)
        {
            try
            {
                var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "LooseLips-memories");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, seed + ".json");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Load this city's memories, once the city exists. Safe to call every frame; it does
        /// nothing until the seed appears and nothing again afterwards.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (!ModConfig.RememberBetweenSessions.Value) return;

            var seed = CurrentSeed();
            if (seed == null || seed == _loadedSeed) return;

            _loadedSeed = seed;
            Threads.Clear();

            var path = PathForSeed(seed);
            if (path == null || !File.Exists(path))
            {
                Plugin.Log.LogInfo("No previous conversations for this city.");
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var stored = JsonSerializer.Deserialize<Dictionary<string, List<StoredLine>>>(json);
                if (stored == null) return;

                foreach (var pair in stored)
                {
                    int id;
                    if (!int.TryParse(pair.Key, out id) || pair.Value == null) continue;

                    var thread = new List<ChatMessage>();
                    foreach (var line in pair.Value)
                    {
                        if (line == null || string.IsNullOrWhiteSpace(line.Text)) continue;
                        thread.Add(line.FromPlayer ? ChatMessage.User(line.Text) : ChatMessage.Assistant(line.Text));
                    }
                    if (thread.Count > 0) Threads[id] = thread;
                }

                Plugin.Log.LogInfo("Recalled conversations with " + Threads.Count + " people in this city.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read this city's conversation memories: " + e.Message);
            }
        }

        public static void Save()
        {
            if (!ModConfig.RememberBetweenSessions.Value) return;

            var seed = _loadedSeed ?? CurrentSeed();
            if (seed == null) return;

            var path = PathForSeed(seed);
            if (path == null) return;

            try
            {
                var stored = new Dictionary<string, List<StoredLine>>();
                foreach (var pair in Threads)
                {
                    if (pair.Value == null || pair.Value.Count == 0) continue;

                    var lines = new List<StoredLine>();
                    foreach (var message in pair.Value)
                    {
                        if (message == null || string.IsNullOrWhiteSpace(message.Content)) continue;
                        lines.Add(new StoredLine
                        {
                            FromPlayer = message.Role == "user",
                            Text = message.Content
                        });
                    }
                    if (lines.Count > 0) stored[pair.Key.ToString()] = lines;
                }

                File.WriteAllText(path, JsonSerializer.Serialize(stored));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not save conversation memories: " + e.Message);
            }
        }

        public sealed class StoredLine
        {
            public bool FromPlayer { get; set; }
            public string Text { get; set; }
        }

        private static string Sanitise(string s)
        {
            var clean = "";
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') clean += c;
            }
            return clean.Length > 0 ? clean : "city";
        }
    }
}
