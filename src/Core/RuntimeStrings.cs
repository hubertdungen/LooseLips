using System;
using System.Collections.Generic;
using Il2CppSystem.Collections.Generic;
using Il2CppDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, Strings.DisplayString>;
using Il2CppTable = Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.Dictionary<string, Strings.DisplayString>>;

namespace LooseLips.Core
{
    /// <summary>
    /// Lets the mod put arbitrary generated text into the game's own string table so
    /// citizens can speak lines that never existed on disk.
    ///
    /// Why this and not the DDS files: a .block carries no text at all, only a GUID that
    /// is resolved against dds.blocks.csv at load time. Rewriting those files would mean
    /// touching game data and reloading. But SpeechController has an overload,
    ///     Speak(string dictionary, string speechEntryRef, ...)
    /// that reads straight from <see cref="Strings.stringTable"/>, and that table is a
    /// plain public static dictionary. Registering a key there is enough for the whole
    /// speech pipeline - bubbles, subtitles and the conversation log - to pick it up.
    ///
    /// Entries live only in memory. Nothing is written to disk and nothing enters a save.
    /// </summary>
    public static class RuntimeStrings
    {
        /// <summary>Name of the in-memory dictionary this mod owns.</summary>
        public const string DictionaryName = "player2.generated";

        private static int _counter;
        private static readonly object Gate = new object();

        /// <summary>Keys we created, so we can clear them when a session ends.</summary>
        private static readonly System.Collections.Generic.List<string> Created =
            new System.Collections.Generic.List<string>();

        /// <summary>
        /// Store <paramref name="text"/> and return the key to hand to
        /// <c>SpeechController.Speak(dictionary, key, ...)</c>. Returns null if the string
        /// table is not up yet, in which case callers should fall back to a scripted line.
        /// </summary>
        public static string Register(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                var table = Strings.stringTable;
                if (table == null)
                {
                    Plugin.Log.LogWarning("Strings.stringTable is not initialised yet; cannot register generated text.");
                    return null;
                }

                string key;
                lock (Gate)
                {
                    _counter++;
                    key = "p2_" + _counter.ToString("X6");
                    Created.Add(key);
                }

                var entry = new Strings.DisplayString
                {
                    displayStr = text,
                    alternateStr = text
                };

                Put(table, DictionaryName, key, entry);

                // The English table is consulted as a fallback when the active language is
                // missing a key, so mirror into it to stay safe on non-English installs.
                var eng = Strings.stringTableENG;
                if (eng != null && !ReferenceEquals(eng, table))
                {
                    Put(eng, DictionaryName, key, entry);
                }

                if (ModConfig.VerboseLogging.Value)
                {
                    Plugin.Log.LogInfo("Registered generated line " + key + ": " + text);
                }

                return key;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Failed to register generated text: " + e);
                return null;
            }
        }

        private static void Put(Il2CppTable table, string dictionary, string key, Strings.DisplayString entry)
        {
            Il2CppDictionary bucket;
            if (!table.TryGetValue(dictionary, out bucket) || bucket == null)
            {
                bucket = new Il2CppDictionary();
                table[dictionary] = bucket;
            }
            bucket[key] = entry;
        }

        /// <summary>
        /// Drop every generated entry. Called when a save is loaded or the city is rebuilt
        /// so the table does not grow without bound across sessions.
        /// </summary>
        public static void Clear()
        {
            try
            {
                lock (Gate)
                {
                    Created.Clear();
                    _counter = 0;
                }

                ClearFrom(Strings.stringTable);
                ClearFrom(Strings.stringTableENG);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not clear generated strings: " + e.Message);
            }
        }

        private static void ClearFrom(Il2CppTable table)
        {
            if (table == null) return;
            Il2CppDictionary bucket;
            if (table.TryGetValue(DictionaryName, out bucket) && bucket != null)
            {
                bucket.Clear();
            }
        }
    }
}
