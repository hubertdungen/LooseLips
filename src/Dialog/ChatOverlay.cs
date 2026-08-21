using System;
using Il2CppInterop.Runtime.Injection;
using LooseLips.Core;
using LooseLips.World;
using UnityEngine;

namespace LooseLips.Dialog
{
    /// <summary>
    /// The typing box, the voice reach meter, and the main-thread pump.
    ///
    /// This is drawn with IMGUI rather than the game's own UI on purpose. The game's
    /// input box is built around fixed-length passcodes, and rebuilding its prefab from
    /// a mod is far more fragile than owning a small overlay outright.
    /// </summary>
    public sealed class ChatOverlay : MonoBehaviour
    {
        public ChatOverlay(IntPtr ptr) : base(ptr) { }

        private static ChatOverlay _instance;

        private static bool _open;
        private static Citizen _target;
        private static bool _shouted;
        private static Action<string> _onSubmit;
        private static string _text = string.Empty;
        private static bool _focusRequested;

        private const int WindowWidth = 620;
        private const int WindowHeight = 132;
        private const string ControlName = "LooseLipsChatInput";

        private static bool _registered;

        /// <summary>
        /// Create the overlay. Called from plugin load rather than from a game hook:
        /// several popular mods patch Toolbox.Start, Harmony runs every postfix for a
        /// method in one chain, and a single throwing postfix skips every one after it.
        /// Sharing a hook with third party code is not a safe place to initialise.
        /// Safe to call more than once.
        /// </summary>
        public static void Install()
        {
            if (_instance != null) return;

            try
            {
                if (!_registered)
                {
                    ClassInjector.RegisterTypeInIl2Cpp<ChatOverlay>();
                    _registered = true;
                }

                var go = new GameObject("LooseLips.ChatOverlay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;

                _instance = go.AddComponent<ChatOverlay>();
                Plugin.Log.LogInfo("Chat overlay installed. Press " +
                    ModConfig.SettingsHotkey.Value + " for settings.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not install the chat overlay: " + e);
            }
        }

        public static void Open(Citizen target, bool shouted, Action<string> onSubmit)
        {
            GameInput.Claim();
            _target = target;
            _shouted = shouted;
            _onSubmit = onSubmit;
            _text = string.Empty;
            _open = true;
            _focusRequested = true;
        }

        public static void Close()
        {
            _open = false;
            if (!SettingsWindow.IsOpen) GameInput.Release();
            _target = null;
            _onSubmit = null;
            _text = string.Empty;
        }

        private void Update()
        {
            // Everything that came back from a network call lands here.
            MainThread.Drain();

            // Recall this city's conversations, and what they achieved, as soon as it exists.
            ConversationMemory.EnsureLoaded();
            WorldMemory.EnsureLoaded();

            // Citizens holding their own conversations, and the lines already queued from one.
            DelayedSpeech.Tick();
            NpcConversation.Tick();
            World.FollowDirector.Tick();
            World.Allegiance.DefendPlayer();
            World.AmbientReactions.Tick();

            // The game reclaims the cursor on its own schedule, so a window that wants the
            // mouse has to keep asking for it.
            if (SettingsWindow.IsOpen || _open) GameInput.Tick();
            else if (GameInput.Held) GameInput.Release();

            if (Input.GetKeyDown(ModConfig.SettingsHotkey.Value))
            {
                // Logged unconditionally: if the window ever fails to appear, the first thing
                // worth knowing is whether the key was seen at all.
                Plugin.Log.LogInfo("Settings hotkey pressed.");

                // Never leave a half typed line behind when opening settings.
                if (_open) Close();
                SettingsWindow.Toggle();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (SettingsWindow.IsOpen) SettingsWindow.Close();
                else if (_open) Close();
            }
        }

        private void OnGUI()
        {
            GUI.depth = -1000;
            SettingsWindow.Draw();

            if (SettingsWindow.IsOpen) return;

            if (_open) DrawInputBox();
            else if (ModConfig.ShowVoiceReachMeter.Value) DrawReachMeter();
        }

        private void DrawInputBox()
        {
            var e = Event.current;

            // Submit on Enter, newline never - this is one spoken line, not a paragraph.
            if (e != null && e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                Submit();
                e.Use();
                return;
            }

            var scale = Mathf.Clamp(ModConfig.UiScale.Value, 0.6f, 2.5f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            var x = (Screen.width / scale - WindowWidth) / 2f;
            var y = Screen.height / scale - WindowHeight - 90f;
            var rect = new Rect(x, y, WindowWidth, WindowHeight);

            var skin = ModConfig.TintTheChatBox.Value ? Skin.Begin() : default;

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(x + 14f, y + 10f, WindowWidth - 28f, WindowHeight - 20f));

            var who = SafeName(_target);
            var header = _shouted ? "Shout at " + who : "Say to " + who;
            var heard = CountListeners();
            if (heard > 0) header += "   (" + heard + " other" + (heard == 1 ? "" : "s") + " within earshot)";

            GUILayout.Label(header);

            GUI.SetNextControlName(ControlName);
            _text = GUILayout.TextField(_text ?? string.Empty, 300, GUILayout.Height(26f));

            if (_focusRequested)
            {
                GUI.FocusControl(ControlName);
                _focusRequested = false;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Enter to speak, Escape to cancel.");
            GUILayout.FlexibleSpace();
            if (!Player2.Player2Client.Available)
            {
                GUI.color = new Color(1f, 0.6f, 0.4f);
                GUILayout.Label("Player2 app not detected");
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            skin.End();

            GUI.matrix = previousMatrix;
        }

        private void DrawReachMeter()
        {
            var player = Player.Instance;
            if (player == null) return;

            int quiet, loud;
            try
            {
                quiet = Earshot.CitizensWhoCanHear(player, false).Count;
                loud = Earshot.CitizensWhoCanHear(player, true).Count;
            }
            catch
            {
                return;
            }

            if (quiet == 0 && loud == 0) return;

            var rect = new Rect(18f, Screen.height - 74f, 260f, 56f);
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, rect.height - 12f));
            GUILayout.Label("Speaking reaches " + quiet);
            GUILayout.Label("Shouting reaches " + loud);
            GUILayout.EndArea();
        }

        private static int CountListeners()
        {
            try
            {
                var player = Player.Instance;
                if (player == null) return 0;

                var count = 0;
                foreach (var c in Earshot.CitizensWhoCanHear(player, _shouted))
                {
                    if (c == null) continue;
                    if (_target != null && c.humanID == _target.humanID) continue;
                    count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static void Submit()
        {
            var line = (_text ?? string.Empty).Trim();
            var callback = _onSubmit;
            Close();

            if (line.Length == 0 || callback == null) return;

            try
            {
                callback(line);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Submitting a line failed: " + ex);
            }
        }

        private static string SafeName(Citizen c)
        {
            try
            {
                return c != null ? c.GetCasualName() : "them";
            }
            catch
            {
                return "them";
            }
        }
    }
}
