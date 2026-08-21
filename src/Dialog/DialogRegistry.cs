using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppSystem.Reflection;
using LooseLips.Core;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Registers our dialogue options with the game and routes their behaviour back to us.
    ///
    /// DialogController keeps a map of DialogPreset to the MethodInfo it invokes when the
    /// option is chosen. There is no way to add a new method to that class from a mod, so
    /// we point our presets at an existing one (WarnNotewriter) and intercept the call.
    /// It is a borrowed doorbell, and it is the approach the Shadows of Doubt modding
    /// community settled on for exactly this reason.
    /// </summary>
    public static class DialogRegistry
    {
        public static readonly Dictionary<string, CustomDialogPreset> Interceptors =
            new Dictionary<string, CustomDialogPreset>();

        private static MethodInfo _borrowedMethod;
        private static bool _presetsBuilt;

        /// <summary>Where the next option goes, so registration order is preserved at the top.</summary>
        private static int _insertedAt;

        /// <summary>
        /// Build the presets. Safe to call more than once; only the first call does work.
        /// Needs Toolbox and the string tables to be loaded, so it runs from a game hook
        /// rather than from plugin load.
        /// </summary>
        public static void BuildPresets()
        {
            if (_presetsBuilt) return;

            var speakMsg = DdsAuthoring.CreateMessage("Say something...", "Player2_SpeakFreely");
            var shoutMsg = DdsAuthoring.CreateMessage("Shout something...", "Player2_Shout");
            if (speakMsg == null || shoutMsg == null) return;   // try again on the next hook

            Register(new SpeakFreelyPreset(speakMsg));
            Register(new ShoutPreset(shoutMsg));

            _presetsBuilt = true;
            Plugin.Log.LogInfo("Dialogue options registered.");
        }

        private static void Register(CustomDialogPreset custom)
        {
            Interceptors[custom.Name] = custom;

            try
            {
                var toolbox = Toolbox.Instance;
                if (toolbox != null)
                {
                    if (toolbox.allDialog != null && !toolbox.allDialog.Contains(custom.Preset))
                        toolbox.allDialog.Add(custom.Preset);

                    // At the top rather than appended. These are the options the mod exists for,
                    // and having to scroll past the vanilla list to reach them every single time
                    // turns a conversation into a menu hunt.
                    if (toolbox.defaultDialogOptions != null && !toolbox.defaultDialogOptions.Contains(custom.Preset))
                        toolbox.defaultDialogOptions.Insert(_insertedAt++, custom.Preset);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not add " + custom.Name + " to the dialog lists: " + e);
            }

            WireInterceptor(custom);
        }

        private static void WireInterceptor(CustomDialogPreset custom)
        {
            if (_borrowedMethod == null) return;   // wired later, once DialogController starts

            try
            {
                var controller = DialogController.Instance;
                if (controller == null || controller.dialogRef == null) return;
                if (!controller.dialogRef.ContainsKey(custom.Preset))
                    controller.dialogRef.Add(custom.Preset, _borrowedMethod);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not wire " + custom.Name + ": " + e);
            }
        }

        private static CustomDialogPreset Lookup(DialogPreset preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.name)) return null;
            CustomDialogPreset custom;
            return Interceptors.TryGetValue(preset.name, out custom) ? custom : null;
        }

        // --- Patches ------------------------------------------------------------

        /// <summary>Grab the method we borrow, then wire anything registered early.</summary>
        [HarmonyPatch(typeof(DialogController), nameof(DialogController.Start))]
        public static class DialogController_Start
        {
            public static void Postfix(DialogController __instance)
            {
                try
                {
                    foreach (var kv in __instance.dialogRef)
                    {
                        if (kv.Key != null && kv.Key.name == "WarnNotewriter")
                        {
                            _borrowedMethod = kv.Value;
                            break;
                        }
                    }

                    if (_borrowedMethod == null)
                    {
                        Plugin.Log.LogError(
                            "Could not find the WarnNotewriter entry in DialogController. " +
                            "Dialogue options will not respond. The game version may have changed.");
                        return;
                    }

                    BuildPresets();

                    foreach (var custom in Interceptors.Values) WireInterceptor(custom);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("DialogController.Start patch failed: " + e);
                }
            }
        }

        /// <summary>Intercept the borrowed method and run our own behaviour instead.</summary>
        [HarmonyPatch(typeof(DialogController), nameof(DialogController.WarnNotewriter))]
        public static class DialogController_WarnNotewriter
        {
            public static bool Prefix(DialogController __instance, Citizen saysTo,
                Interactable saysToInteractable, NewNode where, Actor saidBy, bool success,
                NewRoom roomRef, SideJob jobRef)
            {
                var custom = Lookup(__instance.preset);
                if (custom == null) return true;   // a real WarnNotewriter call; leave it alone

                try
                {
                    custom.RunDialogMethod(__instance, saysTo, saysToInteractable, where, saidBy,
                        success, roomRef, jobRef);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Running " + custom.Name + " failed: " + e);
                }

                return false;
            }
        }

        /// <summary>Decide whether our options show up.</summary>
        [HarmonyPatch(typeof(DialogController), nameof(DialogController.TestSpecialCaseAvailability))]
        public static class DialogController_TestSpecialCaseAvailability
        {
            public static bool Prefix(ref bool __result, DialogPreset preset, Citizen saysTo, SideJob jobRef)
            {
                var custom = Lookup(preset);
                if (custom == null) return true;

                try
                {
                    __result = saysTo != null && custom.IsAvailable(preset, saysTo, jobRef);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Availability check for " + custom.Name + " failed: " + e.Message);
                    __result = false;
                }

                return false;
            }
        }

        /// <summary>Let an option force its own outcome.</summary>
        [HarmonyPatch(typeof(DialogController), nameof(DialogController.ExecuteDialog))]
        public static class DialogController_ExecuteDialog
        {
            public static void Prefix(DialogController __instance, EvidenceWitness.DialogOption dialog,
                Interactable saysTo, NewNode where, Actor saidBy,
                ref DialogController.ForceSuccess forceSuccess)
            {
                if (forceSuccess != DialogController.ForceSuccess.none) return;
                if (dialog == null) return;

                var custom = Lookup(dialog.preset);
                if (custom == null) return;

                Citizen citizen = null;
                try
                {
                    // There is no citizen on the other end of a phone call.
                    if (saysTo != null && saysTo.isActor != null) citizen = saysTo.isActor.TryCast<Citizen>();
                }
                catch { }

                try
                {
                    forceSuccess = custom.ShouldDialogSucceedOverride(__instance, dialog, citizen, where, saidBy);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Success override for " + custom.Name + " failed: " + e.Message);
                }
            }
        }
    }
}
