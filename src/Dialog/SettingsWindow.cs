using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using LooseLips.Core;
using LooseLips.Player2;
using UnityEngine;

namespace LooseLips.Dialog
{
    /// <summary>
    /// The mod's own settings window, opened with a hotkey while playing.
    ///
    /// Everything here writes straight through to the BepInEx config entries, so the
    /// values are the same ones the config file and BepInExConfigManager see, and they
    /// persist without a separate save step. The window exists mainly so the settings
    /// that need a running game to make sense - voice reach, the connection to the
    /// Player2 app - can be adjusted and tested while you are standing in the world.
    /// </summary>
    public static class SettingsWindow
    {
        private const int WindowId = 0x50325732;

        private static bool _open;
        private static Rect _rect = new Rect(120f, 90f, 560f, 640f);
        private static Vector2 _scroll;
        private static int _tab;

        private static readonly string[] Tabs = { "Connection", "Talking", "Consequences", "Debug" };

        private static string _probeResult = "";
        private static string _goalDump = "";
        private static bool _probing;

        public static bool IsOpen => _open;

        public static void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public static void Open()
        {
            if (_open) return;
            _open = true;

            // A window that opened off screen once cannot be found again, because the
            // position is remembered. Pull it back into view every time it opens.
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - 200f));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - 120f));

            GameInput.Claim();
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;

            GameInput.Release();
        }

        public static void Draw()
        {
            if (!_open) return;

            // Draw last, so the game's own HUD cannot end up covering this.
            GUI.depth = -1000;

            var scale = Mathf.Clamp(ModConfig.UiScale.Value, 0.6f, 2.5f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // Tint the chrome rather than everything: fading GUI.color would take the text with
            // it, and a settings window you cannot read is not a transparent one.
            var previousBackground = GUI.backgroundColor;
            var opacity = Mathf.Clamp(ModConfig.WindowOpacity.Value, 0.2f, 1f);
            GUI.backgroundColor = new Color(previousBackground.r, previousBackground.g, previousBackground.b, opacity);

            _rect = GUI.Window(WindowId, _rect, (GUI.WindowFunction)DrawContents, "Loose Lips");

            GUI.backgroundColor = previousBackground;
            GUI.matrix = previousMatrix;
        }

        private static void DrawContents(int id)
        {
            GUILayout.Space(4f);

            _tab = GUILayout.Toolbar(_tab, Tabs);
            GUILayout.Space(6f);

            _scroll = GUILayout.BeginScrollView(_scroll);

            switch (_tab)
            {
                case 0: DrawConnection(); break;
                case 1: DrawTalking(); break;
                case 2: DrawConsequences(); break;
                default: DrawDebug(); break;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Press " + ModConfig.SettingsHotkey.Value + " to close.");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(90f))) Close();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 100000f, 22f));
        }

        // --- Tabs ---------------------------------------------------------------

        private static void DrawConnection()
        {
            Header("Player2 app");

            GUILayout.BeginHorizontal();
            GUILayout.Label(Player2Client.Available ? "Connected" : "Not detected", GUILayout.Width(180f));
            if (GUILayout.Button(_probing ? "Testing..." : "Test connection", GUILayout.Width(150f)) && !_probing)
            {
                _probing = true;
                _probeResult = "";
                var task = Player2Client.ProbeAsync();
                task.ContinueWith(t =>
                {
                    MainThread.Post(() =>
                    {
                        _probing = false;
                        var ok = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result;
                        _probeResult = ok
                            ? "Reached the Player2 app."
                            : "No answer. Is the app running? " + (Player2Client.LastError ?? "");
                    });
                });
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_probeResult)) GUILayout.Label(_probeResult);

            GUILayout.Space(6f);
            TextField(ModConfig.BaseUrl, "Base URL");
            TextField(ModConfig.HealthPath, "Health path");
            TextField(ModConfig.ChatPath, "Chat path");
            TextField(ModConfig.TtsPath, "Text to speech path");
            GUILayout.Label("If a path is wrong, open http://localhost:4315/docs and correct it here.");

            GUILayout.Space(8f);
            Header("Model");
            TextField(ModConfig.Model, "Model name (blank for the default)");
            IntSlider(ModConfig.RequestTimeoutSeconds, "Give up after", 5, 120, " s");
            Toggle(ModConfig.EnableTts, "Speak replies aloud through Player2");
        }

        private static void DrawTalking()
        {
            Header("Conversation");
            IntSlider(ModConfig.HistoryTurnsPerCitizen, "Remembered turns per person", 0, 64, "");
            IntSlider(ModConfig.MaxReplyCharacters, "Longest reply", 60, 600, " characters");
            Toggle(ModConfig.UseVanillaLinesAsInfluence, "Use the game's own lines as tone guidance");
            Toggle(ModConfig.RememberBetweenSessions, "People remember you between sessions");
            GUILayout.Label("The scripted answer is shown to the model as the register to write in, " +
                            "rather than being spoken word for word.");

            GUILayout.Space(10f);
            Header("Voice reach");
            FloatSlider(ModConfig.TalkRadius, "Talking carries", 1f, 30f, " m");
            FloatSlider(ModConfig.ShoutRadius, "Shouting carries", 5f, 90f, " m");
            Toggle(ModConfig.ShowVoiceReachMeter, "Show the reach meter on screen");

            var player = Player.Instance;
            if (player != null)
            {
                try
                {
                    var quiet = World.Earshot.CitizensWhoCanHear(player, false).Count;
                    var loud = World.Earshot.CitizensWhoCanHear(player, true).Count;
                    GUILayout.Space(4f);
                    GUILayout.Label("Right now: speaking reaches " + quiet + ", shouting reaches " + loud + ".");
                }
                catch { }
            }

            GUILayout.Space(10f);
            Header("Citizens talking to each other");
            Toggle(ModConfig.EnableNpcConversations, "Let people near you strike up their own conversations");
            if (ModConfig.EnableNpcConversations.Value)
            {
                Toggle(ModConfig.NpcGossipSpreads, "What they tell each other is genuinely learned");
                FloatSlider(ModConfig.NpcConversationInterval, "Try one every", 20f, 600f, " s");
                IntSlider(ModConfig.NpcConversationLines, "Longest exchange", 2, 8, " lines");
                FloatSlider(ModConfig.NpcConversationLineGap, "Gap between lines", 1f, 10f, " s");
                GUILayout.Label("Last one: " + NpcConversation.LastExchange);
            }
            GUILayout.Label("They only talk where you can hear them. A conversation you cannot overhear has " +
                            "nothing in it for you and still costs a request.");

            GUILayout.Space(10f);
            Header("Interface");
            FloatSlider(ModConfig.UiScale, "Interface scale", 0.6f, 2.5f, "x");
            FloatSlider(ModConfig.WindowOpacity, "Window opacity", 0.2f, 1f, "");
        }

        private static void DrawConsequences()
        {
            Toggle(ModConfig.EnableWorldEffects, "Let conversations change the world");

            if (!ModConfig.EnableWorldEffects.Value)
            {
                GUILayout.Label("Off: people will talk, but nothing they say has any effect.");
                return;
            }

            GUILayout.Space(8f);
            Header("What a convincing line can do");
            Toggle(ModConfig.AllowItemHandover, "Hand over the item they are holding");
            Toggle(ModConfig.AllowPoliceRedirection, "Call police onto you, off you, or onto someone else");
            Toggle(ModConfig.AllowCombatEffects, "Flee, fight, or surrender");
            Toggle(ModConfig.AllowMoneyHandover, "Hand over cash they are carrying");
            IntSlider(ModConfig.MaxMoneyPerLine, "Most one conversation can get", 0, 5000, "");
            Toggle(ModConfig.AllowFollowing, "Agree to come along with you");
            if (ModConfig.AllowFollowing.Value)
            {
                var names = World.FollowDirector.Names();
                GUILayout.Label(names.Count == 0
                    ? "Nobody is with you."
                    : "With you: " + string.Join(", ", names));
            }
            Toggle(ModConfig.AllowTestimony, "Give up where and when they saw somebody");
            GUILayout.Label("Uses the game's own witness mechanism, so what they give you is a real lead in the " +
                            "case file - and they can only name people they actually saw.");
            Toggle(ModConfig.AllowGoalRedirection, "Change what they are doing - send them home, or over to look");
            Toggle(ModConfig.AllowCrowdEffects, "Move everyone in earshot, not just the person you spoke to");

            GUILayout.Space(10f);
            Header("Limits");
            FloatSlider(ModConfig.MaxLikeShiftPerLine, "Most one line can move a relationship", 0f, 1f, "");
            FloatSlider(ModConfig.MaxSuspicionShiftPerLine, "Most one line can move suspicion", 0f, 1f, "");
            GUILayout.Label("Lower values mean it takes a real conversation to win somebody over, " +
                            "rather than a single lucky sentence.");
        }

        private static void DrawDebug()
        {
            Toggle(ModConfig.VerboseLogging, "Verbose logging");
            GUILayout.Label("Writes the reply, how truthful it was, and which effects were applied or " +
                            "discarded into the BepInEx log.");

            GUILayout.Space(8f);
            Toggle(ModConfig.LogPrompts, "Log every prompt");
            GUILayout.Label("Large. Useful when a citizen answers oddly and you want to see what they were told.");

            GUILayout.Space(10f);
            Header("Hotkey");
            GUILayout.Label("Opens this window: " + ModConfig.SettingsHotkey.Value);
            GUILayout.Label("Change it in the config file if you want a different key.");

            GUILayout.Space(10f);
            Header("Does any of this actually work?");
            GUI.enabled = !Core.CoreSelfTest.Running;
            if (GUILayout.Button(Core.CoreSelfTest.Running ? "Testing..." : "Test the whole chain on the nearest person",
                    GUILayout.Width(320f)))
            {
                Core.CoreSelfTest.Run();
            }
            GUI.enabled = true;
            GUILayout.Label(Core.CoreSelfTest.LastSummary);
            GUILayout.Label("Stand next to somebody, then run this. It walks every step - reading what they know, " +
                            "building the prompt, reaching the model, speaking the line, applying the consequences - " +
                            "and names the first one that fails.");

            GUILayout.Space(10f);
            Header("Goal presets");
            if (GUILayout.Button("Write the game's goal list to the transcript", GUILayout.Width(320f)))
            {
                _goalDump = World.GoalDirector.DumpPresetNames();
            }
            if (!string.IsNullOrEmpty(_goalDump)) GUILayout.Label(_goalDump);
            GUILayout.Label("Sending somebody home matches a goal preset by name, and those names live in the " +
                            "game's assets rather than its code. Run this once in a loaded save to see the real " +
                            "list and confirm the matches are right.");

            GUILayout.Space(10f);
            Header("Transcript");
            Toggle(ModConfig.WriteTranscript, "Keep a transcript of every exchange");
            Toggle(ModConfig.TranscribePrompts, "Include the full prompts as well");
            if (!string.IsNullOrEmpty(Core.SessionLog.Path)) GUILayout.Label(Core.SessionLog.Path);

            GUILayout.Space(10f);
            Header("Session");
            if (GUILayout.Button("Forget every conversation", GUILayout.Width(240f)))
            {
                ConversationMemory.Clear();
                VanillaLineCapture.Clear();
                Plugin.Log.LogInfo("Conversation memory cleared from the settings window.");
            }
            GUILayout.Label("People will no longer remember anything you have said to them.");
        }

        // --- Widgets ------------------------------------------------------------

        private static void Header(string text)
        {
            GUILayout.Label("— " + text + " —");
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            var value = GUILayout.Toggle(entry.Value, " " + label);
            if (value != entry.Value) entry.Value = value;
        }

        private static void TextField(ConfigEntry<string> entry, string label)
        {
            GUILayout.Label(label);
            var value = GUILayout.TextField(entry.Value ?? string.Empty, 200);
            if (value != entry.Value) entry.Value = value;
        }

        private static void FloatSlider(ConfigEntry<float> entry, string label, float min, float max, string suffix)
        {
            GUILayout.Label(label + ": " + entry.Value.ToString("0.00") + suffix);
            var value = GUILayout.HorizontalSlider(entry.Value, min, max);
            if (Mathf.Abs(value - entry.Value) > 0.0001f) entry.Value = value;
        }

        private static void IntSlider(ConfigEntry<int> entry, string label, int min, int max, string suffix)
        {
            GUILayout.Label(label + ": " + entry.Value + suffix);
            var value = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max));
            if (value != entry.Value) entry.Value = value;
        }
    }
}
