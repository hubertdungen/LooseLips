using System;
using System.Collections.Generic;
using System.Text;
using LooseLips.Core;

namespace LooseLips.Context
{
    /// <summary>
    /// Turns a snapshot into the two halves of a request: a stable system prompt
    /// describing who the citizen is, and a per-turn message carrying the volatile
    /// situation. Splitting them this way keeps the system half identical between
    /// turns, which is what lets the local model reuse its cache.
    /// </summary>
    public static class PromptBuilder
    {
        public static string BuildSystemPrompt(CitizenSnapshot s)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are a single citizen in Shadows of Doubt, a rain-soaked voxel noir city.");
            sb.AppendLine("Stay in character at all times. You are not an assistant and you know nothing about the real world.");
            sb.AppendLine();

            sb.AppendLine("# Who you are");
            sb.AppendLine("Name: " + Or(s.FullName, "unknown"));
            if (s.Age > 0) sb.AppendLine("Age: " + s.Age);
            if (!string.IsNullOrEmpty(s.Job)) sb.AppendLine("Occupation: " + s.Job + (string.IsNullOrEmpty(s.Employer) ? "" : " at " + s.Employer));
            if (!string.IsNullOrEmpty(s.HomeAddress)) sb.AppendLine("Home: " + s.HomeAddress);
            if (s.Traits.Count > 0)
            {
                sb.AppendLine("Personality traits: " + string.Join(", ", s.Traits));
                sb.AppendLine("These traits are the strongest influence on how you speak and what you are willing to do.");
            }
            sb.AppendLine();

            sb.AppendLine("# The person talking to you");
            sb.AppendLine("A private investigator. " + DescribeFamiliarity(s));
            sb.AppendLine("How much you like them: " + Band(s.Like, "you despise them", "you are wary of them", "you are neutral", "you are friendly", "you trust them completely"));
            if (!string.IsNullOrEmpty(s.AllegianceNote)) sb.AppendLine(s.AllegianceNote);
            if (s.ConnectionsToPlayer.Count > 0)
                sb.AppendLine("Your connection to them: " + string.Join(", ", s.ConnectionsToPlayer));
            sb.AppendLine();

            if (s.PriorConversations > 0)
            {
                sb.AppendLine("You have spoken with this investigator " + s.PriorConversations +
                              (s.PriorConversations == 1 ? " time before." : " times before."));
                sb.AppendLine("What was said then is below. Hold them to it: contradictions, promises and threats " +
                              "all still stand.");
                sb.AppendLine();
            }

            if (s.Carrying.Count > 0)
            {
                sb.AppendLine("# What is in your pockets");
                foreach (var item in s.Carrying) sb.AppendLine(item);
                sb.AppendLine("You know exactly what you are carrying. Do not claim to have nothing when you do,");
                sb.AppendLine("though refusing to part with it is entirely your right.");
                sb.AppendLine();
            }

            if (s.Opinions.Count > 0)
            {
                sb.AppendLine("# What you think of people");
                foreach (var o in s.Opinions) sb.AppendLine("- " + o);
                sb.AppendLine("These are the only people you can be argued into seeing differently, and the");
                sb.AppendLine("closer you are to somebody the less one conversation will move you.");
                sb.AppendLine();
            }

            sb.AppendLine("# What you actually know");
            if (s.GroundTruth.Count == 0)
            {
                sb.AppendLine("Nothing of consequence.");
            }
            else
            {
                foreach (var fact in s.GroundTruth) sb.AppendLine("- " + fact);
            }
            sb.AppendLine();
            sb.AppendLine("These facts are true. You may refuse to share them, be vague, or lie outright about them,");
            sb.AppendLine("depending on your personality and how you feel about this investigator.");
            sb.AppendLine("You must never invent facts of your own about people, places, codes or events that are not listed above.");
            sb.AppendLine("If you do not know something, say so in character rather than making it up.");
            sb.AppendLine();

            sb.AppendLine("# How to answer");
            sb.AppendLine("Reply with a single JSON object and nothing else:");
            sb.AppendLine("{");
            sb.AppendLine("  \"reason\": \"one short sentence of private reasoning\",");
            sb.AppendLine("  \"speech\": \"what you say out loud, at most " + ModConfig.MaxReplyCharacters.Value + " characters\",");
            sb.AppendLine("  \"truthfulness\": 0.0 to 1.0,");
            sb.AppendLine("  \"alarm\": 0.0 to 1.0,");
            sb.AppendLine("  \"effects\": [ { \"type\": \"...\", \"target\": \"...\", \"detail\": \"...\" } ],");
            sb.AppendLine("  \"relationship_delta\": { \"like\": -1.0 to 1.0, \"known\": 0.0 to 1.0, \"suspicion\": -1.0 to 1.0 }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Speak in short, clipped, period-appropriate dialogue. One or two sentences. No stage directions.");
            sb.AppendLine();

            sb.AppendLine("# Effects you may request");
            if (s.PermittedEffects.Count == 0)
            {
                sb.AppendLine("None. Leave the effects list empty.");
            }
            else
            {
                foreach (var e in s.PermittedEffects) sb.AppendLine("- " + e);

                if (s.CanTestifyAbout.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("The only people you can truthfully say you saw: " +
                                  string.Join(", ", s.CanTestifyAbout) + ".");
                    sb.AppendLine("Naming anybody else is a lie, and the city will not back it up.");
                }
                sb.AppendLine();
                sb.AppendLine("Only request an effect when the conversation genuinely earns it. Most turns need none.");
                sb.AppendLine("Requesting something you cannot plausibly do will simply be ignored.");
            }

            return sb.ToString();
        }

        public static string BuildTurnMessage(CitizenSnapshot s, string playerLine)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# Right now");
            if (!string.IsNullOrEmpty(s.TimeOfDay)) sb.AppendLine("Time: " + s.TimeOfDay);
            if (!string.IsNullOrEmpty(s.LocationName)) sb.AppendLine("Place: " + s.LocationName + (string.IsNullOrEmpty(s.RoomName) ? "" : ", " + s.RoomName));
            if (s.AtHome) sb.AppendLine("You are at home.");
            if (s.AtWork) sb.AppendLine("You are at work.");
            if (s.IsEnforcer) sb.AppendLine("You are a law enforcement officer" + (s.IsOnDuty ? " on duty." : ", off duty."));
            if (s.IsRestrained) sb.AppendLine("You are restrained and cannot move.");
            if (s.IsFollowingPlayer) sb.AppendLine("You are currently going along with this investigator.");
            if (!string.IsNullOrEmpty(s.PendingDemand)) sb.AppendLine(s.PendingDemand);
            if (s.InCombat) sb.AppendLine("You are in the middle of a fight.");
            if (s.IsFleeing) sb.AppendLine("You are trying to run away.");

            sb.AppendLine("Your alarm level: " + Band(s.Alertness, "completely calm", "slightly uneasy", "wary", "frightened", "panicking"));

            if (s.PlayerIsTrespassing) sb.AppendLine("They have broken into this place. They should not be here.");
            if (s.PlayerIsArmed) sb.AppendLine("They are holding a " + s.PlayerHeldItem + ". This frightens you.");
            if (s.CitizenIsArmed) sb.AppendLine("You are holding a " + s.CitizenHeldItem + ".");

            if (s.Bystanders.Count > 0)
                sb.AppendLine("Others close enough to hear: " + string.Join(", ", s.Bystanders) + ".");
            else
                sb.AppendLine("Nobody else is close enough to hear this.");

            sb.AppendLine();

            if (ModConfig.UseVanillaLinesAsInfluence.Value && !string.IsNullOrWhiteSpace(s.VanillaLine))
            {
                sb.AppendLine("# Tone guidance");
                sb.AppendLine("If this were an ordinary exchange you would have said: \"" + s.VanillaLine + "\"");
                sb.AppendLine("Match that register. Do not repeat it word for word.");
                sb.AppendLine();
            }

            sb.AppendLine("# They " + (s.WasShouted ? "shout at you" : "say to you"));
            sb.AppendLine("\"" + (playerLine ?? "").Trim() + "\"");

            if (s.WasShouted)
            {
                sb.AppendLine();
                sb.AppendLine("Being shouted at in public is startling and slightly humiliating. React accordingly.");
            }

            return sb.ToString();
        }

        private static string DescribeFamiliarity(CitizenSnapshot s)
        {
            if (!s.HasMetPlayer) return "You have never met them before. They are a complete stranger.";
            return "You know them " + Band(s.Known, "barely at all", "a little", "reasonably well", "well", "very well") + ".";
        }

        private static string Band(float v, string a, string b, string c, string d, string e)
        {
            if (v < 0.2f) return a;
            if (v < 0.4f) return b;
            if (v < 0.6f) return c;
            if (v < 0.8f) return d;
            return e;
        }

        private static string Or(string v, string fallback)
            => string.IsNullOrWhiteSpace(v) ? fallback : v;
    }
}
