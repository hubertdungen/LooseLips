using System;
using LooseLips.Core;
using Il2CppTable = Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.Dictionary<string, Strings.DisplayString>>;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Builds DDS content in memory so our dialogue options get real labels.
    ///
    /// A DialogPreset points at a message id, a message is a list of block ids, and a
    /// block carries no text at all - the text is looked up by block id in the
    /// "dds.blocks" string dictionary. Authoring all three layers at runtime means the
    /// option renders through the game's own path instead of needing UI patches, and
    /// nothing is written to disk.
    /// </summary>
    public static class DdsAuthoring
    {
        /// <summary>The dictionary the game loads dds.blocks.csv into.</summary>
        private const string BlockDictionary = "dds.blocks";

        /// <summary>
        /// Register a one-block message carrying <paramref name="text"/> and return the
        /// message id, ready to assign to <c>DialogPreset.msgID</c>.
        /// Returns null if the game's dictionaries are not ready yet.
        /// </summary>
        public static string CreateMessage(string text, string debugName)
        {
            try
            {
                var toolbox = Toolbox.Instance;
                if (toolbox == null || toolbox.allDDSBlocks == null || toolbox.allDDSMessages == null)
                {
                    Plugin.Log.LogWarning("DDS dictionaries are not loaded yet; cannot author '" + debugName + "'.");
                    return null;
                }

                var blockId = Guid.NewGuid().ToString();
                var messageId = Guid.NewGuid().ToString();

                var block = new DDSSaveClasses.DDSBlockSave
                {
                    id = blockId,
                    name = debugName + "_block"
                };
                toolbox.allDDSBlocks[blockId] = block;

                RegisterBlockText(blockId, text);

                var message = new DDSSaveClasses.DDSMessageSave
                {
                    id = messageId,
                    name = debugName + "_msg"
                };
                message.AddBlock(blockId);
                toolbox.allDDSMessages[messageId] = message;

                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogInfo("Authored DDS message " + debugName + " -> " + messageId);

                return messageId;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Authoring DDS message '" + debugName + "' failed: " + e);
                return null;
            }
        }

        private static void RegisterBlockText(string blockId, string text)
        {
            var entry = new Strings.DisplayString
            {
                displayStr = text,
                alternateStr = text
            };

            Put(Strings.stringTable, blockId, entry);

            var eng = Strings.stringTableENG;
            if (eng != null && !ReferenceEquals(eng, Strings.stringTable))
            {
                Put(eng, blockId, entry);
            }
        }

        private static void Put(Il2CppTable table, string key, Strings.DisplayString entry)
        {
            if (table == null) return;

            Il2CppSystem.Collections.Generic.Dictionary<string, Strings.DisplayString> bucket;
            if (!table.TryGetValue(BlockDictionary, out bucket) || bucket == null)
            {
                bucket = new Il2CppSystem.Collections.Generic.Dictionary<string, Strings.DisplayString>();
                table[BlockDictionary] = bucket;
            }
            bucket[key] = entry;
        }
    }
}
