using System;
using System.Collections.Generic;
using HarmonyLib;
using LooseLips.Core;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Remembers the last scripted line each citizen said, so it can be handed to the
    /// model as tone guidance.
    ///
    /// This is the piece that keeps generated dialogue sounding like Shadows of Doubt
    /// rather than like a chatbot: the hand-written line is never shown verbatim, it is
    /// shown to the model as "this is the register you speak in".
    /// </summary>
    public static class VanillaLineCapture
    {
        private static readonly Dictionary<int, string> LastLine = new Dictionary<int, string>();

        public static void Remember(Citizen citizen, string line)
        {
            if (citizen == null || string.IsNullOrWhiteSpace(line)) return;
            LastLine[citizen.humanID] = line;
        }

        /// <summary>Read and clear the remembered line for a citizen.</summary>
        public static string TakeLastFor(Citizen citizen)
        {
            if (citizen == null || !ModConfig.UseVanillaLinesAsInfluence.Value) return null;

            string line;
            if (!LastLine.TryGetValue(citizen.humanID, out line)) return null;
            LastLine.Remove(citizen.humanID);
            return line;
        }

        public static void Clear() => LastLine.Clear();

        /// <summary>
        /// Watches ordinary DDS speech going past and records the resolved text.
        /// Deliberately a postfix that swallows everything: this is a nicety, and it must
        /// never be able to break normal dialogue.
        /// </summary>
        [HarmonyPatch(typeof(SpeechController), nameof(SpeechController.Speak),
            new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(Human), typeof(SideJob),
                         typeof(Human.InteractionDialogInstance) })]
        public static class SpeechController_Speak_Capture
        {
            public static void Prefix(SpeechController __instance, string ddsMessage)
            {
                if (!ModConfig.UseVanillaLinesAsInfluence.Value) return;
                if (string.IsNullOrEmpty(ddsMessage)) return;

                try
                {
                    var human = __instance.actor as Human;
                    if (human == null)
                    {
                        human = __instance.actor != null ? __instance.actor.TryCast<Human>() : null;
                    }
                    if (human == null || human.isPlayer) return;

                    var citizen = human.TryCast<Citizen>();
                    if (citizen == null) return;

                    Il2CppSystem.Collections.Generic.List<int> groups;
                    var parts = human.ParseDDSMessage(ddsMessage, null, out groups);
                    if (parts == null || parts.Count == 0) return;

                    var text = string.Empty;
                    foreach (var part in parts)
                    {
                        if (string.IsNullOrEmpty(part)) continue;
                        if (text.Length > 0) text += " ";
                        text += part;
                    }

                    Remember(citizen, text);
                }
                catch (Exception e)
                {
                    if (ModConfig.VerboseLogging.Value)
                        Plugin.Log.LogWarning("Could not capture a vanilla line: " + e.Message);
                }
            }
        }
    }
}
