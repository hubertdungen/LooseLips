using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LooseLips.Player2
{
    /// <summary>
    /// Readers that survive the shapes models actually produce, rather than the one the schema
    /// asked for.
    ///
    /// This matters more than it looks. Deserialisation is all or nothing: a single field in an
    /// unexpected shape - "effects": ["flee"] instead of a list of objects, or "truthfulness":
    /// "0.8" as a string - throws, and the whole reply falls back to being treated as plain
    /// prose. A reply treated as prose carries no effects, no relationship movement and no
    /// alarm, so one stray quotation mark silently costs the turn everything it was supposed to
    /// do, and the only symptom in game is a citizen who talks well and never acts.
    ///
    /// So every field that a model can plausibly get slightly wrong is read leniently, and only
    /// genuinely unreadable input is allowed to fail.
    /// </summary>
    internal static class TolerantJson
    {
        /// <summary>Numbers that arrive as strings, bools, or on a 0-100 scale.</summary>
        internal sealed class FlexibleFloat : JsonConverter<float>
        {
            public override float Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.Number:
                        return Scale(reader.GetSingle());

                    case JsonTokenType.String:
                        var text = reader.GetString();
                        float parsed;
                        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                            return Scale(parsed);
                        // Models sometimes answer these fields in words.
                        return WordToNumber(text);

                    case JsonTokenType.True: return 1f;
                    case JsonTokenType.False: return 0f;
                    case JsonTokenType.Null: return 0f;

                    default:
                        reader.Skip();
                        return 0f;
                }
            }

            /// <summary>
            /// These fields are all 0 to 1. A model that answers 80 meant 80 percent, not eighty
            /// times the maximum, and clamping instead would read as total certainty or panic.
            /// </summary>
            private static float Scale(float v) => v > 1f && v <= 100f ? v / 100f : v;

            private static float WordToNumber(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return 0f;
                switch (text.Trim().ToLowerInvariant())
                {
                    case "none": case "no": case "never": return 0f;
                    case "low": case "slight": case "a little": return 0.25f;
                    case "medium": case "moderate": case "some": return 0.5f;
                    case "high": case "very": case "a lot": return 0.75f;
                    case "full": case "total": case "complete": case "yes": return 1f;
                    default: return 0f;
                }
            }

            public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
                => writer.WriteNumberValue(value);
        }

        /// <summary>
        /// Effects, however they were expressed: a list of objects as asked for, a list of bare
        /// names, a single name, a single object, or nothing at all.
        /// </summary>
        internal sealed class FlexibleEffectList : JsonConverter<List<WorldEffect>>
        {
            /// <summary>
            /// Handle null ourselves. Left to the serialiser, an explicit "effects": null sets
            /// the property to null rather than an empty list, and every caller then has to
            /// remember that. Callers get a list either way.
            /// </summary>
            public override bool HandleNull => true;

            public override List<WorldEffect> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                var list = new List<WorldEffect>();

                switch (reader.TokenType)
                {
                    case JsonTokenType.Null:
                        return list;

                    case JsonTokenType.String:
                        Add(list, reader.GetString());
                        return list;

                    case JsonTokenType.StartObject:
                        var single = ReadOne(ref reader, options);
                        if (single != null) list.Add(single);
                        return list;

                    case JsonTokenType.StartArray:
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                Add(list, reader.GetString());
                            }
                            else if (reader.TokenType == JsonTokenType.StartObject)
                            {
                                var item = ReadOne(ref reader, options);
                                if (item != null) list.Add(item);
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                        return list;

                    default:
                        reader.Skip();
                        return list;
                }
            }

            private static void Add(List<WorldEffect> list, string name)
            {
                if (!string.IsNullOrWhiteSpace(name)) list.Add(new WorldEffect { Type = name });
            }

            /// <summary>
            /// One effect object, read field by field so an unknown or oddly typed member cannot
            /// take the rest of the list with it.
            /// </summary>
            private static WorldEffect ReadOne(ref Utf8JsonReader reader, JsonSerializerOptions options)
            {
                var effect = new WorldEffect();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) { reader.Skip(); continue; }

                    var property = reader.GetString();
                    if (!reader.Read()) break;

                    var value = ReadScalar(ref reader);
                    if (string.IsNullOrEmpty(property)) continue;

                    switch (property.ToLowerInvariant())
                    {
                        case "type": case "effect": case "name": case "action":
                            effect.Type = value;
                            break;
                        case "target": case "who": case "person": case "amount":
                            if (string.IsNullOrEmpty(effect.Target)) effect.Target = value;
                            break;
                        case "detail": case "details": case "reason": case "for":
                            effect.Detail = value;
                            break;
                    }
                }

                return string.IsNullOrWhiteSpace(effect.Type) ? null : effect;
            }

            /// <summary>Whatever this value is, get a string out of it without throwing.</summary>
            private static string ReadScalar(ref Utf8JsonReader reader)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String: return reader.GetString();
                    case JsonTokenType.Number:
                        double number;
                        return reader.TryGetDouble(out number)
                            ? number.ToString(CultureInfo.InvariantCulture)
                            : null;
                    case JsonTokenType.True: return "true";
                    case JsonTokenType.False: return "false";
                    case JsonTokenType.Null: return null;
                    default:
                        reader.Skip();
                        return null;
                }
            }

            public override void Write(Utf8JsonWriter writer, List<WorldEffect> value, JsonSerializerOptions options)
                => JsonSerializer.Serialize(writer, value, options);
        }

        /// <summary>
        /// The relationship block, which models sometimes send as a bare number or omit halfway.
        /// </summary>
        internal sealed class FlexibleRelationship : JsonConverter<RelationshipDelta>
        {
            public override RelationshipDelta Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;

                if (reader.TokenType == JsonTokenType.Number)
                {
                    // A single number can only sensibly mean how much better they feel about you.
                    return new RelationshipDelta { Like = reader.GetSingle() };
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    reader.Skip();
                    return null;
                }

                var delta = new RelationshipDelta();
                var floats = new FlexibleFloat();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) { reader.Skip(); continue; }

                    var property = reader.GetString();
                    if (!reader.Read()) break;

                    var value = floats.Read(ref reader, typeof(float), options);
                    if (string.IsNullOrEmpty(property)) continue;

                    switch (property.ToLowerInvariant())
                    {
                        case "like": case "liking": case "affection": delta.Like = value; break;
                        case "known": case "know": case "familiarity": delta.Known = value; break;
                        case "suspicion": case "suspicious": delta.Suspicion = value; break;
                    }
                }

                return delta;
            }

            public override void Write(Utf8JsonWriter writer, RelationshipDelta value, JsonSerializerOptions options)
                => JsonSerializer.Serialize(writer, value, options);
        }
    }
}
