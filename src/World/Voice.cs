using LooseLips.Core;

namespace LooseLips.World
{
    /// <summary>
    /// How loudly something is said, and therefore how far it travels.
    ///
    /// The game itself only knows shouting from not shouting - a single bool on its speech
    /// call. Whispering is this mod's own idea, and it is real in the only way that matters
    /// here: reach. A whispered line is spoken normally by the engine but carries barely past
    /// the person it was meant for, so leaning in to tell somebody something is genuinely
    /// private, and a room full of people will not all overhear it.
    /// </summary>
    public enum VoiceLevel
    {
        Whisper,
        Normal,
        Shout
    }

    public static class Voice
    {
        public static float RadiusOf(VoiceLevel level)
        {
            switch (level)
            {
                case VoiceLevel.Whisper: return ModConfig.WhisperRadius.Value;
                case VoiceLevel.Shout: return ModConfig.ShoutRadius.Value;
                default: return ModConfig.TalkRadius.Value;
            }
        }

        /// <summary>Only a shout carries through into the next room.</summary>
        public static bool CarriesNextDoor(VoiceLevel level) => level == VoiceLevel.Shout;

        public static string Describe(VoiceLevel level)
        {
            switch (level)
            {
                case VoiceLevel.Whisper: return "whispered";
                case VoiceLevel.Shout: return "shouted";
                default: return "spoken";
            }
        }

        /// <summary>Read a level out of whatever word the model used.</summary>
        public static VoiceLevel Parse(string written, VoiceLevel fallback = VoiceLevel.Normal)
        {
            if (string.IsNullOrWhiteSpace(written)) return fallback;

            switch (written.Trim().ToLowerInvariant())
            {
                case "whisper": case "whispered": case "whispering":
                case "quiet": case "quietly": case "under_my_breath": case "murmur":
                    return VoiceLevel.Whisper;

                case "shout": case "shouted": case "shouting": case "yell": case "yelled":
                case "scream": case "screamed": case "loud": case "loudly": case "call_out":
                    return VoiceLevel.Shout;

                case "normal": case "speak": case "spoken": case "say": case "said": case "talk":
                    return VoiceLevel.Normal;

                default:
                    return fallback;
            }
        }

        /// <summary>The old two-state view, for the parts of the mod that still think in shouts.</summary>
        public static VoiceLevel FromShouted(bool shouted) => shouted ? VoiceLevel.Shout : VoiceLevel.Normal;

        public static bool IsShout(VoiceLevel level) => level == VoiceLevel.Shout;
    }
}
