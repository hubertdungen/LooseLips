using System;
using LooseLips.Core;

namespace LooseLips.Dialog
{
    /// <summary>
    /// Puts generated text through the game's own speech pipeline.
    ///
    /// SpeechController has an overload that takes a dictionary name and an entry key
    /// rather than a DDS GUID. <see cref="RuntimeStrings"/> registers the generated line
    /// under that dictionary a moment earlier, so from the game's point of view this is
    /// an ordinary line of dialogue: it gets a bubble, a duration, subtitles, and it is
    /// heard by whoever is nearby.
    /// </summary>
    public static class SpeechRelay
    {
        /// <summary>The player says something out loud.</summary>
        public static void PlayerSaid(Citizen listener, string line, bool shouted)
        {
            var player = Player.Instance;
            if (player == null) return;
            Say(player, listener != null ? listener.interactable : null, line, shouted, interupt: true);
        }

        /// <summary>A citizen replies.</summary>
        public static void CitizenSays(Citizen speaker, string line, bool shouted)
        {
            if (speaker == null) return;
            var player = Player.Instance;
            Say(speaker, player != null ? player.interactable : null, line, shouted, interupt: true);
        }

        /// <summary>
        /// One citizen speaking to another. Passing the listener as the target is what makes the
        /// game aim the bubble at them rather than at the player, so an overheard conversation
        /// reads as something you walked in on instead of something addressed to you.
        /// </summary>
        public static void CitizenSaysTo(Citizen speaker, Citizen listener, string line)
        {
            if (speaker == null) return;
            Say(speaker, listener != null ? listener.interactable : null, line, shouted: false, interupt: false);
        }

        /// <summary>
        /// A citizen speaking at a chosen volume. The engine only has shouting and not
        /// shouting, so a whisper is delivered as ordinary speech - what makes it a whisper is
        /// that the rest of the mod treats its reach as barely past arm's length.
        /// </summary>
        public static void CitizenSaysAt(Citizen speaker, string line, World.VoiceLevel level)
        {
            if (speaker == null) return;
            Say(speaker, null, line, World.Voice.IsShout(level), interupt: false);
        }

        /// <summary>Placeholder beat while the model is still generating.</summary>
        public static void ShowThinking(Citizen speaker)
        {
            if (speaker == null) return;
            Say(speaker, null, "...", shouted: false, interupt: false);
        }

        /// <summary>Shown when Player2 is unreachable or returned nothing usable.</summary>
        public static void ShowUnavailable(Citizen speaker)
        {
            if (speaker == null) return;
            var line = Player2Client_Available()
                ? "..."
                : "Sorry, I got nothing to say to you.";
            Say(speaker, null, line, shouted: false, interupt: true);
        }

        private static bool Player2Client_Available()
        {
            try { return Player2.Player2Client.Available; }
            catch { return false; }
        }

        private static void Say(Actor speaker, Interactable speakingTo, string line, bool shouted, bool interupt)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(line)) return;

            try
            {
                var controller = speaker.speechController;
                if (controller == null)
                {
                    Plugin.Log.LogWarning("No speech controller on " + speaker.name + "; line dropped.");
                    return;
                }

                var key = RuntimeStrings.Register(line);
                if (key == null) return;

                controller.Speak(
                    RuntimeStrings.DictionaryName,
                    key,
                    useParsing: false,     // the text is already final; no DDS token substitution
                    shout: shouted,
                    interupt: interupt,
                    delay: 0f,
                    forceColour: false,
                    color: default,
                    speakingAbout: null,
                    endsDialog: false,
                    jobHandIn: false,
                    sideJob: null,
                    dialogPreset: null,
                    dialog: null,
                    speakingTo: speakingTo,
                    interactionInstance: null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Speaking a generated line failed: " + e);
            }
        }
    }
}
