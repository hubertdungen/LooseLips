using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LooseLips.Core;

namespace LooseLips.Player2
{
    /// <summary>
    /// Talks to the Player2 desktop app over its local HTTP API.
    /// Everything here runs off the Unity main thread; callers marshal results back
    /// through <see cref="MainThread"/>.
    /// </summary>
    public static class Player2Client
    {
        private static readonly HttpClient Http = new HttpClient();
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static Timer _heartbeat;

        /// <summary>True once a health probe has succeeded. Reset if the app goes away.</summary>
        public static bool Available { get; private set; }

        public static string LastError { get; private set; }

        public static void Initialise()
        {
            Http.Timeout = TimeSpan.FromSeconds(ModConfig.RequestTimeoutSeconds.Value);

            // Player2 uses this header for revenue attribution; it is not a secret.
            Http.DefaultRequestHeaders.Remove("player2-game-key");
            Http.DefaultRequestHeaders.Add("player2-game-key", ModConfig.GameKey.Value);

            // The docs ask for a heartbeat roughly every 60 seconds while the game is running.
            _heartbeat = new Timer(_ => _ = ProbeAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
        }

        public static void Shutdown()
        {
            _heartbeat?.Dispose();
            _heartbeat = null;
        }

        private static string Url(string path)
        {
            var b = ModConfig.BaseUrl.Value.TrimEnd('/');
            if (!path.StartsWith("/")) path = "/" + path;
            return b + path;
        }

        /// <summary>
        /// How many credits are left, and on what tier. Cheap, and worth knowing: on a well
        /// stocked account it is invisible, but on a free one it decides whether the mod should
        /// be generating background chatter at all.
        /// </summary>
        public static async Task ReadBalanceAsync()
        {
            try
            {
                using var resp = await Http.GetAsync(Url("/v1/joules")).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Player2Status.Saw((int)resp.StatusCode);
                    return;
                }

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var reading = JsonSerializer.Deserialize<JouleReading>(json, JsonOpts);
                if (reading != null) Player2Status.Reading(reading.Joules, reading.Tier);
            }
            catch
            {
                // A missing balance is not an error worth interrupting anybody over.
            }
        }

        /// <summary>Health check plus keep-alive. Safe to call repeatedly.</summary>
        public static async Task<bool> ProbeAsync()
        {
            try
            {
                using var resp = await Http.GetAsync(Url(ModConfig.HealthPath.Value)).ConfigureAwait(false);
                var ok = resp.IsSuccessStatusCode;
                if (ok) await ReadBalanceAsync().ConfigureAwait(false);
                else Player2Status.Saw((int)resp.StatusCode);
                if (ok != Available)
                {
                    var msg = ok
                        ? "Player2 app is reachable."
                        : "Player2 health check returned " + (int)resp.StatusCode + ".";
                    MainThread.Post(() => Plugin.Log.LogInfo(msg));
                }
                Available = ok;
                return ok;
            }
            catch (Exception e)
            {
                if (Available)
                {
                    MainThread.Post(() => Plugin.Log.LogWarning(
                        "Lost contact with the Player2 app. Free-form dialogue will fall back to vanilla lines."));
                }
                Available = false;
                Player2Status.Unreachable();
                LastError = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Ask the model for a citizen's reply. Returns null when the request fails, which
        /// callers treat as "fall back to the scripted line".
        /// </summary>
        public static async Task<NpcReply> GenerateReplyAsync(
            string systemPrompt, IReadOnlyList<ChatMessage> history, string userTurn, CancellationToken ct = default)
        {
            var req = new ChatRequest
            {
                Model = string.IsNullOrWhiteSpace(ModConfig.Model.Value) ? null : ModConfig.Model.Value,
                Temperature = 0.85f,
                MaxTokens = 400
            };
            req.Messages.Add(ChatMessage.System(systemPrompt));
            if (history != null) req.Messages.AddRange(history);
            req.Messages.Add(ChatMessage.User(userTurn));

            string raw;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var body = JsonSerializer.Serialize(req, JsonOpts);
                if (ModConfig.LogPrompts.Value)
                {
                    MainThread.Post(() => Plugin.Log.LogInfo("[prompt] " + systemPrompt + "\n[turn] " + userTurn));
                }

                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = await Http.PostAsync(Url(ModConfig.ChatPath.Value), content, ct).ConfigureAwait(false);

                Player2Status.Saw((int)resp.StatusCode);

                if (!resp.IsSuccessStatusCode)
                {
                    var errBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LastError = (int)resp.StatusCode + ": " + Truncate(errBody, 400);
                    var captured = LastError;
                    MainThread.Post(() => Plugin.Log.LogWarning("Player2 chat request failed - " + captured));
                    return Failed(clock, captured);
                }

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<ChatResponse>(json, JsonOpts);
                raw = parsed?.Choices != null && parsed.Choices.Count > 0 ? parsed.Choices[0].Message?.Content : null;

                if (ModConfig.VerboseLogging.Value)
                {
                    var shown = Truncate(raw, 800);
                    MainThread.Post(() => Plugin.Log.LogInfo("[raw reply] " + shown));
                }
            }
            catch (OperationCanceledException)
            {
                return Failed(clock, "timed out after " + ModConfig.RequestTimeoutSeconds.Value + " s");
            }
            catch (Exception e)
            {
                LastError = e.Message;
                MainThread.Post(() => Plugin.Log.LogWarning("Player2 chat request threw: " + e.Message));
                return Failed(clock, e.Message);
            }

            clock.Stop();
            var parsed2 = ParseReply(raw);
            if (parsed2 != null) parsed2.LatencyMs = clock.ElapsedMilliseconds;
            return parsed2;
        }

        /// <summary>
        /// A reply object carrying nothing but the failure. Callers still get the timing and the
        /// reason, which is what makes a dead endpoint distinguishable from a slow one in the
        /// transcript instead of both showing up as silence.
        /// </summary>
        private static NpcReply Failed(System.Diagnostics.Stopwatch clock, string reason)
        {
            clock.Stop();
            return new NpcReply { Speech = null, Raw = reason, WellFormed = false, LatencyMs = clock.ElapsedMilliseconds };
        }

        /// <summary>
        /// Models wrap JSON in prose or fences more often than not, so pull out the first
        /// balanced object rather than trusting the whole string.
        /// </summary>
        public static NpcReply ParseReply(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var candidate = ExtractJsonObject(raw);
            if (candidate != null)
            {
                try
                {
                    var reply = JsonSerializer.Deserialize<NpcReply>(candidate, JsonOpts);
                    if (reply != null && !string.IsNullOrWhiteSpace(reply.Speech))
                    {
                        reply.Speech = Sanitise(reply.Speech);
                        reply.Raw = raw;
                        reply.WellFormed = true;
                        return reply;
                    }
                }
                catch (JsonException)
                {
                    // fall through to the plain-text path
                }
            }

            // The model ignored the schema. Treat the whole thing as a spoken line so the
            // conversation still works instead of dropping the turn.
            return new NpcReply { Speech = Sanitise(raw), Truthfulness = 1f, Raw = raw, WellFormed = false };
        }

        /// <summary>
        /// Same tolerance as <see cref="ParseReply"/>: models fence and pad their JSON, and an
        /// overheard conversation is not worth dropping over a stray sentence around it.
        /// </summary>
        public static NpcExchange ParseExchange(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var candidate = ExtractJsonObject(raw);
            if (candidate == null) return null;

            try
            {
                var exchange = JsonSerializer.Deserialize<NpcExchange>(candidate, JsonOpts);
                if (exchange?.Lines == null) return null;

                foreach (var line in exchange.Lines)
                {
                    if (line != null) line.Says = Sanitise(line.Says);
                }
                return exchange;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string ExtractJsonObject(string s)
        {
            var fence = Regex.Match(s, "```(?:json)?\\s*(\\{[\\s\\S]*?\\})\\s*```", RegexOptions.IgnoreCase);
            if (fence.Success) return fence.Groups[1].Value;

            var start = s.IndexOf('{');
            if (start < 0) return null;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < s.Length; i++)
            {
                var c = s[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return s.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        /// <summary>Strip markup and clamp length so the line fits a speech bubble.</summary>
        private static string Sanitise(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            s = s.Trim();
            s = Regex.Replace(s, "^```(?:json)?|```$", "").Trim();
            s = Regex.Replace(s, "\\s+", " ");
            // The game's text renderer treats these as rich-text and pipe-delimited tokens.
            s = s.Replace("|", "/").Replace("<", "(").Replace(">", ")");

            // Models reach for typographic punctuation by habit. The game's font is not
            // guaranteed to have those glyphs, and a missing one shows as a box mid-sentence.
            s = FoldPunctuation(s);

            var max = ModConfig.MaxReplyCharacters.Value;
            if (s.Length > max)
            {
                var cut = s.LastIndexOfAny(new[] { '.', '!', '?' }, Math.Min(max, s.Length - 1));
                s = cut > max / 2 ? s.Substring(0, cut + 1) : s.Substring(0, max).TrimEnd() + "...";
            }
            return s;
        }

        /// <summary>
        /// Fold typographic punctuation down to ASCII. Models reach for em dashes and curly
        /// quotes by habit, and the game's font is not guaranteed to carry those glyphs - a
        /// missing one shows up as a box in the middle of a spoken line. Written with character
        /// codes rather than literals so this file stays ASCII.
        /// </summary>
        private static string FoldPunctuation(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case (char)0x2014:  // em dash
                    case (char)0x2013:  // en dash
                    case (char)0x2212:  // minus
                        sb.Append('-');
                        break;
                    case (char)0x2018:  // left single quote
                    case (char)0x2019:  // right single quote / apostrophe
                        sb.Append((char)0x27);
                        break;
                    case (char)0x201C:  // left double quote
                    case (char)0x201D:  // right double quote
                        sb.Append('"');
                        break;
                    case (char)0x2026:  // ellipsis
                        sb.Append("...");
                        break;
                    case (char)0x00A0:  // non-breaking space
                        sb.Append(' ');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString().Trim('"');
        }

        private static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }

        /// <summary>Fire-and-forget text to speech. Failures are logged and ignored.</summary>
        public static async Task SpeakAsync(string text, string voiceId = null)
        {
            if (!ModConfig.EnableTts.Value || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var req = new TtsRequest
                {
                    Text = text,
                    PlayInApp = true,
                    Speed = ModConfig.TtsSpeed.Value
                };
                if (!string.IsNullOrWhiteSpace(voiceId)) req.VoiceIds = new List<string> { voiceId };
                var body = JsonSerializer.Serialize(req, JsonOpts);
                using var content = new StringContent(body, Encoding.UTF8, "application/json");

                // Synthesis is an order of magnitude slower than generation - tens of seconds for a
                // single sentence - so it cannot share the chat deadline or it would always abort.
                using var deadline = new CancellationTokenSource(
                    TimeSpan.FromSeconds(ModConfig.TtsTimeoutSeconds.Value));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                using var resp = await Http.PostAsync(Url(ModConfig.TtsPath.Value), content, deadline.Token)
                                           .ConfigureAwait(false);
                clock.Stop();
                if (ModConfig.VerboseLogging.Value)
                {
                    var ms = clock.ElapsedMilliseconds;
                    MainThread.Post(() => Plugin.Log.LogInfo("Spoken line synthesised in " + ms + " ms."));
                }
                if (!resp.IsSuccessStatusCode)
                {
                    // Worth a warning even when quiet: a silent 400 here is exactly how a wrong
                    // request body hides, which is what happened to the first version of this call.
                    var code = (int)resp.StatusCode;
                    var detail = Truncate(await resp.Content.ReadAsStringAsync().ConfigureAwait(false), 300);
                    MainThread.Post(() => Plugin.Log.LogWarning("TTS request returned " + code + ": " + detail));
                }
            }
            catch (Exception e)
            {
                if (ModConfig.VerboseLogging.Value)
                {
                    MainThread.Post(() => Plugin.Log.LogWarning("TTS request threw: " + e.Message));
                }
            }
        }
    }
}
