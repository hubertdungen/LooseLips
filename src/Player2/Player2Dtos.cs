using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LooseLips.Player2
{
    // --- Wire format for the OpenAI-compatible chat endpoint Player2 exposes locally. ---

    public sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; }
        [JsonPropertyName("content")] public string Content { get; set; }

        public static ChatMessage System(string c) => new ChatMessage { Role = "system", Content = c };
        public static ChatMessage User(string c) => new ChatMessage { Role = "user", Content = c };
        public static ChatMessage Assistant(string c) => new ChatMessage { Role = "assistant", Content = c };
    }

    public sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; }
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.85f;
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 400;
    }

    public sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage Message { get; set; }
    }

    public sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<ChatChoice> Choices { get; set; }
    }

    /// <summary>
    /// Player2 calls this SingleTextToSpeechRequest. text, play_in_app and speed are all
    /// required, and voices are a list even when there is only one - blending several is a
    /// documented feature. Getting any of that wrong returns a 400 with no audio.
    /// </summary>
    public sealed class TtsRequest
    {
        [JsonPropertyName("text")] public string Text { get; set; }
        [JsonPropertyName("play_in_app")] public bool PlayInApp { get; set; } = true;
        [JsonPropertyName("speed")] public float Speed { get; set; } = 1f;
        [JsonPropertyName("voice_ids")] public List<string> VoiceIds { get; set; }
    }

    /// <summary>One entry from GET /v1/tts/voices.</summary>
    public sealed class Voice
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("language")] public string Language { get; set; }
        [JsonPropertyName("gender")] public string Gender { get; set; }
    }

    public sealed class VoiceList
    {
        [JsonPropertyName("voices")] public List<Voice> Voices { get; set; }
    }

    // --- The structured reply we ask the model to produce. ---

    /// <summary>
    /// One requested change to the world. Every field is advisory: the executor validates
    /// each effect against real game state before anything happens.
    /// </summary>
    public sealed class WorldEffect
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("target")] public string Target { get; set; }
        [JsonPropertyName("detail")] public string Detail { get; set; }
    }

    public sealed class RelationshipDelta
    {
        [JsonPropertyName("like")] public float Like { get; set; }
        [JsonPropertyName("known")] public float Known { get; set; }
        [JsonPropertyName("suspicion")] public float Suspicion { get; set; }
    }

    public sealed class NpcReply
    {
        /// <summary>Model's private reasoning. Logged, never shown in-game.</summary>
        [JsonPropertyName("reason")] public string Reason { get; set; }

        /// <summary>The line the citizen actually says.</summary>
        [JsonPropertyName("speech")] public string Speech { get; set; }

        /// <summary>0 = deliberate lie, 1 = fully honest. Drives which facts may be fabricated.</summary>
        [JsonPropertyName("truthfulness")] public float Truthfulness { get; set; } = 1f;

        /// <summary>How rattled the citizen is by what was said, 0 to 1.</summary>
        [JsonPropertyName("alarm")] public float Alarm { get; set; }

        [JsonPropertyName("effects")] public List<WorldEffect> Effects { get; set; } = new List<WorldEffect>();

        [JsonPropertyName("relationship_delta")] public RelationshipDelta RelationshipDelta { get; set; }

        /// <summary>
        /// Exactly what the model sent back, before parsing. Kept so a playtest transcript can
        /// show a malformed or off-schema answer rather than just its salvaged remains.
        /// </summary>
        [JsonIgnore] public string Raw { get; set; }

        /// <summary>True when the schema parsed; false when we fell back to treating prose as speech.</summary>
        [JsonIgnore] public bool WellFormed { get; set; }

        /// <summary>Round trip to the model in milliseconds.</summary>
        [JsonIgnore] public long LatencyMs { get; set; }
    }

    // --- An overheard exchange between two citizens. ---

    public sealed class ExchangeLine
    {
        [JsonPropertyName("who")] public string Who { get; set; }
        [JsonPropertyName("says")] public string Says { get; set; }
    }

    /// <summary>One of them mentioning that they saw somebody, which the other then knows.</summary>
    public sealed class GossipItem
    {
        [JsonPropertyName("teller")] public string Teller { get; set; }
        [JsonPropertyName("about")] public string About { get; set; }
    }

    public sealed class NpcExchange
    {
        [JsonPropertyName("lines")] public List<ExchangeLine> Lines { get; set; }
        [JsonPropertyName("gossip")] public GossipItem Gossip { get; set; }
    }
}
