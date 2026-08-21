using LooseLips.Core;
using UnityEngine;

namespace LooseLips.Dialog
{
    /// <summary>
    /// The look of the mod's own windows.
    ///
    /// IMGUI has no theming to speak of - it has two global tints, one for the chrome and one
    /// for the content, and everything else is the game's skin. That constraint decides the
    /// design here: rather than pretend to be a styling system, each theme is a pair of
    /// colours, and the transparency is applied to the chrome alone. Fading the content tint
    /// instead would take the text with it, and a settings window you cannot read is not a
    /// transparent one.
    /// </summary>
    public static class Skin
    {
        /// <summary>Restores whatever was in place before <see cref="Begin"/>.</summary>
        public struct Scope
        {
            public Color Background;
            public Color Content;
            public bool Applied;

            public void End()
            {
                if (!Applied) return;
                GUI.backgroundColor = Background;
                GUI.contentColor = Content;
            }
        }

        public static Scope Begin()
        {
            var scope = new Scope
            {
                Background = GUI.backgroundColor,
                Content = GUI.contentColor,
                Applied = true
            };

            var theme = ModConfig.Theme.Value;
            if (theme == "Game default")
            {
                // Still honour opacity: it is the one thing people want regardless of colour.
                var plain = GUI.backgroundColor;
                GUI.backgroundColor = new Color(plain.r, plain.g, plain.b, Opacity());
                return scope;
            }

            var tint = Shift(BaseColour(theme));
            GUI.backgroundColor = new Color(tint.r, tint.g, tint.b, Opacity());
            GUI.contentColor = ContentColour(theme);
            return scope;
        }

        private static float Opacity() => Mathf.Clamp(ModConfig.WindowOpacity.Value, 0.2f, 1f);

        private static Color BaseColour(string theme)
        {
            switch (theme)
            {
                case "Neon":  return new Color(0.30f, 0.82f, 0.88f);   // the icon's cyan
                case "Amber": return new Color(0.95f, 0.68f, 0.30f);   // lamplight
                case "Paper": return new Color(0.92f, 0.90f, 0.86f);   // case files
                default:      return new Color(0.42f, 0.55f, 0.72f);   // Rain: wet blue night
            }
        }

        private static Color ContentColour(string theme)
            => theme == "Paper" ? new Color(0.10f, 0.10f, 0.12f) : Color.white;

        /// <summary>
        /// Rotate the theme colour around the hue wheel. Cheap to implement and it covers the
        /// real request behind "let me change the colour" without inventing a colour picker
        /// IMGUI would struggle to draw.
        /// </summary>
        private static Color Shift(Color c)
        {
            var amount = ModConfig.AccentHue.Value;
            if (Mathf.Abs(amount) < 0.001f) return c;

            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            h = Mathf.Repeat(h + amount, 1f);
            return Color.HSVToRGB(h, s, v);
        }
    }
}
