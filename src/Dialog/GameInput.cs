using System;
using LooseLips.Core;
using UnityEngine;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Takes the mouse away from the game, and gives it back.
    ///
    /// Setting UnityEngine.Cursor directly does not work here. The game drives the cursor
    /// through its own InputController every frame, so anything set from outside is
    /// overwritten before the next repaint - which looks exactly like a window that will
    /// not take clicks while the character keeps turning underneath it. The fix is to ask
    /// the game rather than fight it, and to keep asking for as long as a window is open,
    /// because any interaction on the game's side can hand the mouse back to the player.
    /// </summary>
    public static class GameInput
    {
        private static bool _held;

        public static bool Held => _held;

        /// <summary>Claim the mouse for a mod window. Safe to call when already held.</summary>
        public static void Claim()
        {
            _held = true;
            Apply(true);
        }

        /// <summary>Give the mouse back to the game.</summary>
        public static void Release()
        {
            if (!_held) return;
            _held = false;
            Apply(false);
        }

        /// <summary>
        /// Reassert the claim. Called every frame while a window is open: the game reclaims
        /// the cursor on its own schedule, so claiming once is not enough.
        /// </summary>
        public static void Tick()
        {
            if (_held) Apply(true);
        }

        private static void Apply(bool ours)
        {
            try
            {
                var input = InputController.Instance;
                if (input != null)
                {
                    input.SetMouseInputMode(ours, true);
                    input.SetCursorVisible(ours);
                    input.SetCursorLock(!ours);

                    // The master switch for the game's own keys. Without it, typing a line
                    // containing "f" opens the investigation board mid-sentence, because the
                    // game never stopped listening for its shortcuts.
                    input.enableInput = !ours;
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Could not change the game's mouse mode: " + e.Message);
            }

            try
            {
                // Stop the character walking off while you are typing or reading settings.
                // Movement and mouse-look are separate switches: disabling only the first left
                // the camera still turning with the mouse, which is what made the window feel
                // like it had not taken the mouse at all.
                var player = Player.Instance;
                if (player != null)
                {
                    player.EnablePlayerMovement(!ours);
                    player.EnablePlayerMouseLook(!ours, false);
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                    Plugin.Log.LogWarning("Could not suspend player movement: " + e.Message);
            }

            // Belt and braces: if the game is not running yet there is no InputController,
            // and the plain Unity cursor is all there is.
            try
            {
                Cursor.lockState = ours ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = ours;
            }
            catch { }
        }
    }
}
