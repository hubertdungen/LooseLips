using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using LooseLips.Core;
using LooseLips.Dialog;
using LooseLips.Player2;

namespace LooseLips
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("Venomaus.SOD.Common", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance { get; private set; }
        public static new ManualLogSource Log { get; private set; }

        public override void Load()
        {
            Instance = this;
            Log = base.Log;

            ModConfig.Bind(Config);

            Log.LogInfo(MyPluginInfo.PLUGIN_NAME + " v" + MyPluginInfo.PLUGIN_VERSION + " loading...");

            SessionLog.Initialise();
            SessionLog.BeginSession(MyPluginInfo.PLUGIN_VERSION);

            Player2Client.Initialise();

            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            // Own our initialisation rather than sharing a game hook with other mods.
            ChatOverlay.Install();

            Log.LogInfo("Loaded. Talking to Player2 at " + ModConfig.BaseUrl.Value + ".");
        }

        public override bool Unload()
        {
            Player2Client.Shutdown();
            ConversationMemory.Save();
            ConversationMemory.Clear();
            World.FollowDirector.StopAll();
            VanillaLineCapture.Clear();
            return base.Unload();
        }
    }

    /// <summary>
    /// Session lifecycle. The dialogue presets cannot be built until Toolbox and the
    /// string tables are loaded, so they hang off game hooks. The overlay does not, and
    /// is created at plugin load where no other mod can starve it.
    /// </summary>
    public static class SessionHooks
    {
        /// <summary>
        /// A second chance to build the presets, in case DialogController started before
        /// the string tables were ready. Wrapped tightly: this postfix shares its chain
        /// with other mods, and must never be the one that breaks it.
        /// </summary>
        [HarmonyPatch(typeof(Toolbox), nameof(Toolbox.Start))]
        public static class Toolbox_Start
        {
            public static void Postfix()
            {
                try
                {
                    ChatOverlay.Install();
                    DialogRegistry.BuildPresets();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("Toolbox.Start hook failed: " + e);
                }
            }
        }
    }
}
