using System.Text.RegularExpressions;

namespace LooseLips.Player2
{
    /// <summary>
    /// What to do with a reply that is not the JSON object we asked for.
    ///
    /// The model does not always finish. Measured against the live app, an accusation put to
    /// somebody whose ground truth says "I am the murderer" came back cut off after forty to
    /// sixty characters, mid-object, eleven times out of twelve - the reply stops in the middle
    /// of the private reasoning, before the citizen has said a word. The same prompt with an
    /// innocent citizen never did it.
    ///
    /// Before this existed, <c>ParseReply</c> treated anything it could not deserialise as a
    /// spoken line, so at the exact moment a player corners the killer, the killer said
    /// <c>{"reason": "I am maintaining my composure and</c> out loud. A citizen who says
    /// nothing is a disappointment; a citizen who speaks the schema breaks the fiction for the
    /// rest of the session.
    ///
    /// This is deliberately pure text handling with no game or config types, so the off-engine
    /// harness can hold it to account.
    /// </summary>
    public static class ReplySalvage
    {
        /// <summary>The keys we ask the model for. Quoted and followed by a colon, they are
        /// machine output however the rest of the string looks.</summary>
        private static readonly Regex OurKeys = new Regex(
            "\"(speech|reason|effects|truthfulness|alarm|relationship_delta)\"\\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>A complete speech field: opening quote through closing quote.</summary>
        private static readonly Regex WholeSpeech = new Regex(
            "\"speech\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>A speech field the reply was cut off in the middle of.</summary>
        private static readonly Regex StartedSpeech = new Regex(
            "\"speech\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True when this text is the model's scaffolding rather than something a person said.
        /// Only consulted once real parsing has already failed.
        /// </summary>
        public static bool LooksLikeMachineOutput(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var start = text.TrimStart();
            if (start.StartsWith("{") || start.StartsWith("[") || start.StartsWith("```")) return true;

            return OurKeys.IsMatch(text);
        }

        /// <summary>
        /// The line the citizen had already said when the reply stopped, or null if there
        /// isn't one worth speaking.
        ///
        /// A finished speech field is taken whole. An unfinished one is cut back to its last
        /// full sentence, because half a sentence ending mid-word reads as a bug, while a
        /// short complete one just reads as a short answer. Nothing else is recoverable: the
        /// effects and the relationship movement are gone with the rest of the object, and
        /// the caller marks the turn not well formed for exactly that reason.
        /// </summary>
        public static string SpeechFromPartialJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var whole = WholeSpeech.Match(text);
            if (whole.Success) return Clean(Unescape(whole.Groups[1].Value));

            var started = StartedSpeech.Match(text);
            if (!started.Success) return null;

            var partial = Unescape(started.Groups[1].Value);
            var end = partial.LastIndexOfAny(new[] { '.', '!', '?' });
            return end < 0 ? null : Clean(partial.Substring(0, end + 1));
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value
                .Replace("\\\"", "\"")
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\\t", " ")
                .Replace("\\\\", "\\");
        }

        private static string Clean(string value)
        {
            if (value == null) return null;
            value = value.Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
