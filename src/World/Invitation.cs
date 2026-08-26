using System;

namespace LooseLips.World
{
    /// <summary>
    /// Did the player ask for this, or are they just making conversation?
    ///
    /// Some effects are answers rather than remarks. Watching a citizen recite everyone they
    /// saw that evening because the player typed "Hmmm...." is the single most immersion-
    /// breaking thing this mod does: it turns small talk into a case file being read aloud.
    /// The sightings have to be in the prompt for a real question to be answerable at all,
    /// and a model handed a list will find a reason to read it out, so the judgement is made
    /// here instead of being asked of the model.
    ///
    /// The test errs heavily towards letting things through. Only a line that is short *and*
    /// carries no question mark *and* contains nothing that reads as asking counts as
    /// uninvited - which is exactly the filler the bug showed up on. Anything longer, or
    /// anything with a question in it, is the player's business and passes untouched. Players
    /// type in their own language, and a wrongly blocked question is a far worse failure than
    /// an unprompted answer.
    /// </summary>
    public static class Invitation
    {
        /// <summary>Longer than this and it is a real turn, whatever words are in it.</summary>
        private const int FillerWordLimit = 6;

        /// <summary>
        /// Words that mean somebody is being asked something, in any arrangement.
        ///
        /// "know" is deliberately absent. It reads as asking in "do you know him", but that
        /// line is already caught by "do", and the commoner short use is the player closing a
        /// subject down - "I dont know them" - which is the opposite of an invitation, and was
        /// one of the lines this bug showed up on.
        /// </summary>
        private static readonly string[] Asking =
        {
            "who", "what", "where", "when", "why", "which", "how",
            "did", "do ", "does", "have", "has", "is ", "are ", "was ", "were ", "can ", "could",
            "tell", "say", "ask", "saw", "seen", "see", "spot", "notice",
            "remember", "describe", "name", "anyone", "anybody", "someone", "somebody",
            "quem", "onde", "quando", "porque", "viu", "diga", "diz",
        };

        /// <summary>True unless the line is plainly filler.</summary>
        public static bool WasInvited(string playerLine)
        {
            if (string.IsNullOrWhiteSpace(playerLine)) return false;

            var line = playerLine.Trim();
            if (line.IndexOf('?') >= 0) return true;

            var words = line.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > FillerWordLimit) return true;

            var lower = " " + line.ToLowerInvariant() + " ";
            foreach (var word in Asking)
            {
                if (lower.Contains(word)) return true;
            }

            return false;
        }
    }
}
