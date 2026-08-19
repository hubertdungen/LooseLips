using System;
using System.Collections.Generic;
using LooseLips.Core;

namespace LooseLips.World
{
    /// <summary>
    /// Every effect the world understands, each declared once.
    ///
    /// This used to be three lists that had to agree: a block of strings describing the
    /// vocabulary to the model, a switch dispatching those names, and the config flags gating
    /// both. Nothing enforced that they matched, so an effect could be offered and never
    /// handled, or handled under a name nothing ever offered - and the symptom of either is a
    /// citizen who says they will do something and does not.
    ///
    /// Here a name, its description, its config gate, its aliases and its handler are one
    /// object. The vocabulary sent to the model is generated from the same list that dispatches
    /// it, so the two cannot drift apart, and adding an effect means adding one entry.
    /// </summary>
    public static class EffectCatalogue
    {
        /// <summary>What an effect is asked to do, and to whom.</summary>
        public sealed class Request
        {
            public Citizen Speaker;
            public string Target;
            public string Detail;
            public bool Shouted;
        }

        public sealed class Definition
        {
            /// <summary>Canonical name, as offered to the model.</summary>
            public string Name;

            /// <summary>How it is explained to the model. Empty means it is accepted but never offered.</summary>
            public string Description;

            /// <summary>Whether the player has this switched on.</summary>
            public Func<bool> Enabled = () => true;

            /// <summary>Runs it. Returns null when it happened, or a short reason why not.</summary>
            public Func<Request, string> Run;

            /// <summary>Other spellings the model might use.</summary>
            public string[] Aliases = new string[0];

            /// <summary>
            /// Effects in the same group contradict each other. A citizen cannot flee and attack
            /// in one breath; when both are asked for, the first survives and the rest are
            /// refused with a reason rather than applied in whatever order they arrived.
            /// </summary>
            public string Conflicts;

            public bool Offered => Enabled() && !string.IsNullOrEmpty(Description);
        }

        private static readonly List<Definition> All = new List<Definition>();
        private static readonly Dictionary<string, Definition> ByName = new Dictionary<string, Definition>();

        public static void Register(Definition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name) || definition.Run == null) return;

            All.Add(definition);
            ByName[Normalise(definition.Name)] = definition;
            foreach (var alias in definition.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias)) ByName[Normalise(alias)] = definition;
            }
        }

        public static IEnumerable<Definition> Offered()
        {
            foreach (var definition in All)
            {
                if (definition.Offered) yield return definition;
            }
        }

        /// <summary>Look up whatever the model wrote. Null when it is not an effect at all.</summary>
        public static Definition Find(string written)
        {
            if (string.IsNullOrWhiteSpace(written)) return null;

            Definition found;
            return ByName.TryGetValue(Normalise(written), out found) ? found : null;
        }

        /// <summary>
        /// Fold the many ways a model writes the same name onto one key: "Give Money",
        /// "give-money", "giveMoney" and "GIVE_MONEY" all mean give_money.
        /// </summary>
        public static string Normalise(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";

            var chars = new List<char>(s.Length);
            var previousWasLower = false;

            foreach (var raw in s.Trim())
            {
                var c = raw;

                if (c == ' ' || c == '-' || c == '.' || c == '/')
                {
                    if (chars.Count > 0 && chars[chars.Count - 1] != '_') chars.Add('_');
                    previousWasLower = false;
                    continue;
                }

                if (char.IsUpper(c))
                {
                    // camelCase boundary becomes a separator, so giveMoney folds onto give_money.
                    if (previousWasLower && chars.Count > 0 && chars[chars.Count - 1] != '_') chars.Add('_');
                    chars.Add(char.ToLowerInvariant(c));
                    previousWasLower = false;
                    continue;
                }

                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    chars.Add(c);
                    previousWasLower = char.IsLower(c);
                    continue;
                }

                // Anything else - quotes, punctuation - is noise.
            }

            var result = new string(chars.ToArray()).Trim('_');
            return result;
        }

        public static void Reset()
        {
            All.Clear();
            ByName.Clear();
        }

        public static int Count => All.Count;
    }
}
