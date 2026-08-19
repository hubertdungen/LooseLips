using BepInEx.Configuration;
using UnityEngine;

namespace LooseLips.Core
{
    /// <summary>
    /// All user-tunable settings. Bound once from <see cref="Plugin.Load"/>.
    /// </summary>
    public static class ModConfig
    {
        // --- Player2 connection ---
        public static ConfigEntry<string> BaseUrl;
        public static ConfigEntry<string> GameKey;
        public static ConfigEntry<string> ChatPath;
        public static ConfigEntry<string> HealthPath;
        public static ConfigEntry<string> TtsPath;
        public static ConfigEntry<string> Model;
        public static ConfigEntry<int> RequestTimeoutSeconds;
        public static ConfigEntry<bool> EnableTts;
        public static ConfigEntry<float> TtsSpeed;
        public static ConfigEntry<int> TtsTimeoutSeconds;

        // --- Conversation behaviour ---
        public static ConfigEntry<int> HistoryTurnsPerCitizen;
        public static ConfigEntry<int> MaxReplyCharacters;
        public static ConfigEntry<bool> UseVanillaLinesAsInfluence;
        public static ConfigEntry<bool> RememberBetweenSessions;

        // --- Voice reach ---
        public static ConfigEntry<float> WhisperRadius;
        public static ConfigEntry<float> TalkRadius;
        public static ConfigEntry<float> ShoutRadius;
        public static ConfigEntry<bool> ShowVoiceReachMeter;

        // --- Citizens talking to each other ---
        public static ConfigEntry<bool> EnableNpcConversations;
        public static ConfigEntry<bool> NpcGossipSpreads;
        public static ConfigEntry<float> NpcConversationInterval;
        public static ConfigEntry<float> NpcConversationCooldown;
        public static ConfigEntry<int> NpcConversationLines;
        public static ConfigEntry<float> NpcConversationLineGap;

        // --- Ambient life ---
        public static ConfigEntry<bool> EnableAmbientLife;
        public static ConfigEntry<int> MaxAmbientPerHour;
        public static ConfigEntry<float> MinSecondsBetweenAmbient;
        public static ConfigEntry<float> PerCitizenCooldown;
        public static ConfigEntry<float> AlarmJumpToReact;

        // --- World effects ---
        public static ConfigEntry<bool> EnableWorldEffects;
        public static ConfigEntry<bool> AllowItemHandover;
        public static ConfigEntry<bool> AllowPoliceRedirection;
        public static ConfigEntry<bool> AllowCombatEffects;
        public static ConfigEntry<bool> AllowTestimony;
        public static ConfigEntry<bool> AllowGoalRedirection;
        public static ConfigEntry<bool> AllowCrowdEffects;
        public static ConfigEntry<bool> AllowMoneyHandover;
        public static ConfigEntry<int> MaxMoneyPerLine;
        public static ConfigEntry<bool> AllowFollowing;
        public static ConfigEntry<int> MaxFollowers;
        public static ConfigEntry<float> FollowDuration;
        public static ConfigEntry<float> FollowNudgeInterval;
        public static ConfigEntry<float> FollowGiveUpDistance;
        public static ConfigEntry<bool> AllowAllegiance;
        public static ConfigEntry<bool> AlliesDefendYou;
        public static ConfigEntry<float> AllyLikeThreshold;
        public static ConfigEntry<float> AllyNerveThreshold;
        public static ConfigEntry<bool> AllowNegotiation;
        public static ConfigEntry<int> MaxDemand;
        public static ConfigEntry<float> DemandExpiry;
        public static ConfigEntry<float> PaymentGoodwill;
        public static ConfigEntry<float> MaxLikeShiftPerLine;
        public static ConfigEntry<float> MaxSuspicionShiftPerLine;

        // --- Interface ---
        public static ConfigEntry<KeyCode> SettingsHotkey;
        public static ConfigEntry<float> UiScale;
        public static ConfigEntry<float> WindowOpacity;

        // --- Debug ---
        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<bool> LogPrompts;
        public static ConfigEntry<bool> WriteTranscript;
        public static ConfigEntry<bool> TranscribePrompts;

        public static void Bind(ConfigFile cfg)
        {
            BaseUrl = cfg.Bind("Player2", "Base URL", "http://localhost:4315",
                "Root URL of the local Player2 desktop app.");
            GameKey = cfg.Bind("Player2", "Game key", "shadows-of-doubt-player2",
                "Sent as the player2-game-key header, used by Player2 for attribution.");
            ChatPath = cfg.Bind("Player2", "Chat endpoint path", "/v1/chat/completions",
                "Path appended to the base URL for chat completions. Player2 exposes an OpenAI-compatible endpoint here. " +
                "If your Player2 build differs, check http://localhost:4315/docs and correct this.");
            HealthPath = cfg.Bind("Player2", "Health endpoint path", "/v1/health",
                "Path used for the availability probe and the keep-alive heartbeat.");
            TtsPath = cfg.Bind("Player2", "TTS endpoint path", "/v1/tts/speak",
                "Path used to speak a generated line aloud. Only used when TTS is enabled.");
            Model = cfg.Bind("Player2", "Model", "",
                "Model name to request. Leave empty to let Player2 pick its default.");
            RequestTimeoutSeconds = cfg.Bind("Player2", "Request timeout (seconds)", 25,
                new ConfigDescription("Give up on a generation after this long.", new AcceptableValueRange<int>(5, 120)));
            EnableTts = cfg.Bind("Player2", "Speak replies aloud", false,
                "Send generated lines to Player2's text-to-speech. Measured on this machine, Player2 needs the best " +
                "part of a minute to synthesise one short line, so the audio lands long after the conversation has " +
                "moved on. Babbler speaks instantly and is the better choice until that changes.");
            TtsTimeoutSeconds = cfg.Bind("Player2", "Text to speech timeout (seconds)", 90,
                new ConfigDescription("Speech synthesis is far slower than generation, and shares no deadline with it.",
                    new AcceptableValueRange<int>(10, 300)));
            TtsSpeed = cfg.Bind("Player2", "Speech speed", 1f,
                new ConfigDescription("How fast spoken replies are read out. The API accepts 0.25 to 4.",
                    new AcceptableValueRange<float>(0.25f, 4f)));

            HistoryTurnsPerCitizen = cfg.Bind("Conversation", "Remembered turns per citizen", 12,
                new ConfigDescription("How much of your conversation with each citizen is replayed to the model.",
                    new AcceptableValueRange<int>(0, 64)));
            MaxReplyCharacters = cfg.Bind("Conversation", "Maximum reply length", 240,
                new ConfigDescription("Replies longer than this are trimmed so they fit a speech bubble.",
                    new AcceptableValueRange<int>(60, 600)));
            UseVanillaLinesAsInfluence = cfg.Bind("Conversation", "Use vanilla lines as influence", true,
                "Feed the game's own scripted answer to the model as tone guidance instead of showing it verbatim.");

            RememberBetweenSessions = cfg.Bind("Conversation", "People remember you between sessions", true,
                "Conversations are kept on disk per city, so somebody you talked into something yesterday does " +
                "not greet you as a stranger today. Stored in BepInEx/LooseLips-memories, keyed by city seed.");

            WhisperRadius = cfg.Bind("Voice reach", "Whispering radius (metres)", 2f,
                new ConfigDescription("How far a whisper carries. The game only knows shouting from not " +
                    "shouting, so whispering is this mod's own idea and it is real in the way that counts: reach.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
            TalkRadius = cfg.Bind("Voice reach", "Talking radius (metres)", 6f,
                new ConfigDescription("How far a normal spoken line carries.", new AcceptableValueRange<float>(1f, 30f)));
            ShoutRadius = cfg.Bind("Voice reach", "Shouting radius (metres)", 22f,
                new ConfigDescription("How far a shout carries.", new AcceptableValueRange<float>(5f, 90f)));
            ShowVoiceReachMeter = cfg.Bind("Voice reach", "Show the reach meter", true,
                "Draw an on-screen indicator of who can currently hear you.");

            EnableNpcConversations = cfg.Bind("Overheard", "Let citizens talk to each other", false,
                "Pairs of people standing near you strike up their own conversations, generated the same way " +
                "yours are. Off by default: each exchange is a real request and takes a few seconds.");
            NpcGossipSpreads = cfg.Bind("Overheard", "Gossip actually spreads", true,
                "When one of them mentions seeing somebody, the other genuinely learns it and can be asked " +
                "about it afterwards. This is what makes an overheard conversation worth standing around for.");
            NpcConversationInterval = cfg.Bind("Overheard", "Try this often (seconds)", 90f,
                new ConfigDescription("How long between attempts to start one.",
                    new AcceptableValueRange<float>(20f, 600f)));
            NpcConversationCooldown = cfg.Bind("Overheard", "Same pair cooldown (seconds)", 600f,
                new ConfigDescription("Stops the same two people talking in circles.",
                    new AcceptableValueRange<float>(60f, 3600f)));
            NpcConversationLines = cfg.Bind("Overheard", "Longest exchange", 4,
                new ConfigDescription("Lines per conversation.", new AcceptableValueRange<int>(2, 8)));
            NpcConversationLineGap = cfg.Bind("Overheard", "Gap between lines (seconds)", 3.5f,
                new ConfigDescription("Spacing, so they take turns instead of talking over each other.",
                    new AcceptableValueRange<float>(1f, 10f)));

            EnableAmbientLife = cfg.Bind("Ambient life", "People react to what happens around them", false,
                "Citizens near you say something when they notice a crime, get frightened, start a fight or " +
                "bolt - written for who they are and what they saw, rather than picked from a list. Off by " +
                "default: every line is a few seconds of your own machine's time.");
            MaxAmbientPerHour = cfg.Bind("Ambient life", "Most reactions per hour of play", 40,
                new ConfigDescription("A hard ceiling, whatever else is going on.",
                    new AcceptableValueRange<int>(0, 400)));
            MinSecondsBetweenAmbient = cfg.Bind("Ambient life", "Shortest gap between reactions (seconds)", 25f,
                new ConfigDescription("Only one is ever generated at a time; this is the floor between them.",
                    new AcceptableValueRange<float>(5f, 300f)));
            PerCitizenCooldown = cfg.Bind("Ambient life", "Same person again after (seconds)", 120f,
                new ConfigDescription("Stops one startled neighbour narrating your entire evening.",
                    new AcceptableValueRange<float>(15f, 900f)));
            AlarmJumpToReact = cfg.Bind("Ambient life", "Fright needed to set somebody off", 0.25f,
                new ConfigDescription("How far their alarm must jump in one go before they say something.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            EnableWorldEffects = cfg.Bind("World effects", "Enable world effects", true,
                "Master switch. When off, conversations are purely cosmetic.");
            AllowItemHandover = cfg.Bind("World effects", "Allow handing over items and keys", true,
                "A convinced citizen may give you something they actually carry.");
            AllowPoliceRedirection = cfg.Bind("World effects", "Allow calling or redirecting police", true,
                "A convinced or frightened citizen may report you, or someone else.");
            AllowCombatEffects = cfg.Bind("World effects", "Allow fleeing and combat", true,
                "Lets speech trigger the game's native flee, surrender and combat responses.");
            AllowTestimony = cfg.Bind("World effects", "Allow giving up what they saw", true,
                "A cornered or willing citizen tells you where and when they saw somebody, through the game's own " +
                "witness mechanism, so it lands in your case file as a real lead rather than just a line of text.");
            AllowGoalRedirection = cfg.Bind("World effects", "Allow changing what people are doing", true,
                "Talk somebody into going home, leaving, or coming to look at something. This rewrites their AI goal, " +
                "which is the difference between changing their mood and changing their afternoon.");
            AllowCrowdEffects = cfg.Bind("World effects", "Allow effects on everyone in earshot", true,
                "Lets one line move the whole room rather than one person. This is what shouting is for.");

            AllowMoneyHandover = cfg.Bind("World effects", "Allow handing over cash", true,
                "A convinced, frightened or bribed citizen can give you money they are genuinely carrying.");
            MaxMoneyPerLine = cfg.Bind("World effects", "Most cash one conversation can get", 200,
                new ConfigDescription("Ceiling per handover, so one lucky sentence cannot empty a wallet.",
                    new AcceptableValueRange<int>(0, 5000)));
            AllowFollowing = cfg.Bind("World effects", "Allow talking people into following you", true,
                "The game has no companion behaviour, so this is built by repeatedly sending them to where you " +
                "are standing. They trail you rather than stick to you, and give up if you outrun them.");
            MaxFollowers = cfg.Bind("World effects", "Most people following at once", 2,
                new ConfigDescription("", new AcceptableValueRange<int>(1, 8)));
            FollowDuration = cfg.Bind("World effects", "They follow for (seconds)", 300f,
                new ConfigDescription("How long somebody stays with you before drifting back to their own life.",
                    new AcceptableValueRange<float>(30f, 1800f)));
            FollowNudgeInterval = cfg.Bind("World effects", "Re-point followers every (seconds)", 4f,
                new ConfigDescription("Lower is tighter following and more work for the AI.",
                    new AcceptableValueRange<float>(1f, 15f)));
            FollowGiveUpDistance = cfg.Bind("World effects", "They give up beyond (metres)", 45f,
                new ConfigDescription("Outrun somebody by this much and they stop bothering.",
                    new AcceptableValueRange<float>(10f, 200f)));

            AllowAllegiance = cfg.Bind("World effects", "Allow people to take sides", true,
                "Somebody can be talked into siding with you, or turned against you. Liking you is a feeling; " +
                "taking your side is a decision, and it is tracked separately.");
            AlliesDefendYou = cfg.Bind("World effects", "Allies step in when you are attacked", true,
                "An ally who is close enough, and not already panicking, will go after whoever is attacking you.");
            AllyLikeThreshold = cfg.Bind("World effects", "Liking needed before somebody sides with you", 0.6f,
                new ConfigDescription("Below this they refuse, however good the argument.",
                    new AcceptableValueRange<float>(0f, 1f)));
            AllyNerveThreshold = cfg.Bind("World effects", "Allies too frightened to help above", 0.75f,
                new ConfigDescription("An ally more alarmed than this stays out of it.",
                    new AcceptableValueRange<float>(0f, 1f)));

            AllowNegotiation = cfg.Bind("World effects", "Allow haggling and paying people", true,
                "Citizens can name a price for what they know, and be paid it out of your own money. " +
                "The price has to be named in one turn and settled in another, so nothing can be invented " +
                "and paid in the same breath.");
            MaxDemand = cfg.Bind("World effects", "Most anyone will ask for", 500,
                new ConfigDescription("", new AcceptableValueRange<int>(0, 10000)));
            DemandExpiry = cfg.Bind("World effects", "A price stands for (seconds)", 180f,
                new ConfigDescription("How long they wait before the offer is off.",
                    new AcceptableValueRange<float>(30f, 1200f)));
            PaymentGoodwill = cfg.Bind("World effects", "Goodwill bought by paying up", 0.2f,
                new ConfigDescription("How much being paid in full improves how they feel about you.",
                    new AcceptableValueRange<float>(0f, 1f)));

            MaxLikeShiftPerLine = cfg.Bind("World effects", "Maximum like shift per line", 0.15f,
                new ConfigDescription("Caps how much one sentence can move a relationship.",
                    new AcceptableValueRange<float>(0f, 1f)));
            MaxSuspicionShiftPerLine = cfg.Bind("World effects", "Maximum suspicion shift per line", 0.25f,
                new ConfigDescription("Caps how much one sentence can move suspicion.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SettingsHotkey = cfg.Bind("Interface", "Settings window hotkey", KeyCode.F4,
                "Opens this mod's settings window in game. F5 is quick save, so avoid it.");
            UiScale = cfg.Bind("Interface", "Interface scale", 1f,
                new ConfigDescription("Scales the mod's own windows. Raise it on a high resolution screen.",
                    new AcceptableValueRange<float>(0.6f, 2.5f)));

            WindowOpacity = cfg.Bind("Interface", "Window opacity", 1f,
                new ConfigDescription("How solid this mod's windows are. Lower it to keep an eye on the street " +
                    "behind the settings window.", new AcceptableValueRange<float>(0.2f, 1f)));

            VerboseLogging = cfg.Bind("Debug", "Verbose logging", false, "Log the full request and response cycle.");
            LogPrompts = cfg.Bind("Debug", "Log prompts", false, "Write every prompt sent to the model into the BepInEx log.");
            WriteTranscript = cfg.Bind("Debug", "Write a transcript", true,
                "Keep a readable record of every exchange in BepInEx/LooseLips-transcript.log: what was said, " +
                "how long the model took, what it asked for, and what the game allowed. This is the file to " +
                "look at when a conversation feels wrong but nothing errors.");
            TranscribePrompts = cfg.Bind("Debug", "Put prompts in the transcript", false,
                "Also write the full prompt each citizen was given. Large, but it is the only way to tell a bad " +
                "answer from a bad question.");
        }
    }
}
