using System;
using System.Collections.Generic;
using System.Text.Json;
using LooseLips.Player2;
using LooseLips.World;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(string what, bool ok, string detail = "")
    {
        if (ok) { _passed++; Console.WriteLine("  ok   " + what); }
        else { _failed++; Console.WriteLine("  FAIL " + what + (detail.Length > 0 ? "  -> " + detail : "")); }
    }

    private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private static NpcReply Parse(string json) => JsonSerializer.Deserialize<NpcReply>(json, Opts);

    private static void Main()
    {
        Console.WriteLine("Name folding");
        Check("give_money stays itself", EffectCatalogue.Normalise("give_money") == "give_money");
        Check("\"Give Money\" folds", EffectCatalogue.Normalise("Give Money") == "give_money",
            EffectCatalogue.Normalise("Give Money"));
        Check("\"give-money\" folds", EffectCatalogue.Normalise("give-money") == "give_money",
            EffectCatalogue.Normalise("give-money"));
        Check("\"giveMoney\" folds", EffectCatalogue.Normalise("giveMoney") == "give_money",
            EffectCatalogue.Normalise("giveMoney"));
        Check("\"GIVE_MONEY\" folds", EffectCatalogue.Normalise("GIVE_MONEY") == "give_money",
            EffectCatalogue.Normalise("GIVE_MONEY"));
        Check("\" give money. \" folds", EffectCatalogue.Normalise("  give money.  ") == "give_money",
            EffectCatalogue.Normalise("  give money.  "));
        Check("quoted name folds", EffectCatalogue.Normalise("\"give_money\"") == "give_money",
            EffectCatalogue.Normalise("\"give_money\""));

        Console.WriteLine();
        Console.WriteLine("Effects in whatever shape the model sent them");

        var asObjects = Parse("{\"speech\":\"x\",\"effects\":[{\"type\":\"flee\"},{\"type\":\"attack\",\"target\":\"Otto\"}]}");
        Check("list of objects", asObjects.Effects.Count == 2 && asObjects.Effects[1].Target == "Otto");

        var asStrings = Parse("{\"speech\":\"x\",\"effects\":[\"flee\",\"give_money\"]}");
        Check("list of bare names", asStrings.Effects.Count == 2 && asStrings.Effects[0].Type == "flee",
            asStrings.Effects.Count.ToString());

        var single = Parse("{\"speech\":\"x\",\"effects\":\"flee\"}");
        Check("one bare name", single.Effects.Count == 1 && single.Effects[0].Type == "flee");

        var oneObject = Parse("{\"speech\":\"x\",\"effects\":{\"type\":\"flee\"}}");
        Check("one object, unwrapped", oneObject.Effects.Count == 1);

        var mixed = Parse("{\"speech\":\"x\",\"effects\":[\"flee\",{\"type\":\"give_money\",\"target\":50}]}");
        Check("mixed list", mixed.Effects.Count == 2 && mixed.Effects[1].Target == "50",
            mixed.Effects.Count + " / " + (mixed.Effects.Count > 1 ? mixed.Effects[1].Target : "-"));

        var otherKeys = Parse("{\"speech\":\"x\",\"effects\":[{\"action\":\"flee\",\"who\":\"Otto\"}]}");
        Check("alternative field names", otherKeys.Effects.Count == 1 &&
                                         otherKeys.Effects[0].Type == "flee" &&
                                         otherKeys.Effects[0].Target == "Otto");

        var junkInside = Parse("{\"speech\":\"x\",\"effects\":[{\"type\":\"flee\",\"extra\":{\"a\":[1,2]}}]}");
        Check("unknown nested member ignored", junkInside.Effects.Count == 1);

        var nullEffects = Parse("{\"speech\":\"x\",\"effects\":null}");
        Check("null effects", nullEffects.Effects != null && nullEffects.Effects.Count == 0);

        Console.WriteLine();
        Console.WriteLine("Numbers the model got slightly wrong");

        var stringNumber = Parse("{\"speech\":\"x\",\"truthfulness\":\"0.8\",\"alarm\":\"0.3\"}");
        Check("numbers as strings", Math.Abs(stringNumber.Truthfulness - 0.8f) < 0.001f,
            stringNumber.Truthfulness.ToString());

        var percent = Parse("{\"speech\":\"x\",\"alarm\":80}");
        Check("0-100 scale rescaled", Math.Abs(percent.Alarm - 0.8f) < 0.001f, percent.Alarm.ToString());

        var words = Parse("{\"speech\":\"x\",\"alarm\":\"high\"}");
        Check("a word instead of a number", Math.Abs(words.Alarm - 0.75f) < 0.001f, words.Alarm.ToString());

        var boolish = Parse("{\"speech\":\"x\",\"truthfulness\":true}");
        Check("true instead of 1", Math.Abs(boolish.Truthfulness - 1f) < 0.001f);

        Console.WriteLine();
        Console.WriteLine("The relationship block");

        var normal = Parse("{\"speech\":\"x\",\"relationship_delta\":{\"like\":-0.2,\"known\":0.1}}");
        Check("as specified", Math.Abs(normal.RelationshipDelta.Like + 0.2f) < 0.001f);

        var bare = Parse("{\"speech\":\"x\",\"relationship_delta\":-0.3}");
        Check("a bare number", bare.RelationshipDelta != null &&
                               Math.Abs(bare.RelationshipDelta.Like + 0.3f) < 0.001f);

        var synonyms = Parse("{\"speech\":\"x\",\"relationship_delta\":{\"liking\":\"0.4\",\"familiarity\":0.2}}");
        Check("synonyms and strings", synonyms.RelationshipDelta != null &&
                                      Math.Abs(synonyms.RelationshipDelta.Like - 0.4f) < 0.001f,
            synonyms.RelationshipDelta == null ? "null" : synonyms.RelationshipDelta.Like.ToString());

        Console.WriteLine();
        Console.WriteLine("The whole reply survives one bad field");

        var oneBadField = Parse("{\"reason\":\"r\",\"speech\":\"I saw nothing.\",\"truthfulness\":\"eighty percent\"," +
                                "\"alarm\":0.4,\"effects\":[\"flee\"],\"relationship_delta\":{\"like\":-0.1}}");
        Check("speech kept", oneBadField.Speech == "I saw nothing.");
        Check("effects kept despite the bad field", oneBadField.Effects.Count == 1);
        Check("relationship kept", oneBadField.RelationshipDelta != null);

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "All " + _passed + " checks passed."
            : _passed + " passed, " + _failed + " FAILED.");
        Environment.Exit(_failed == 0 ? 0 : 1);
    }
}
