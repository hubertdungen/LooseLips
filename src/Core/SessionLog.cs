using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LooseLips.Core
{
    /// <summary>
    /// A human-readable transcript of everything the mod does, written beside the BepInEx log.
    ///
    /// This exists because the interesting failures in this mod are not exceptions. A citizen
    /// who answers blandly, hands over nothing and forgets you a minute later is working
    /// exactly as coded and still wrong. The only way to tell a bad prompt from a bad model
    /// from a rejected effect is to see the whole exchange side by side afterwards, so every
    /// turn is written out in full: what the citizen was told, how long the model took, what
    /// it asked for, and what the game actually allowed.
    ///
    /// Writes are appended and flushed per exchange. A playtest is short and the file is small;
    /// losing the last few lines to a crash would defeat the purpose.
    /// </summary>
    public static class SessionLog
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _headerWritten;

        public static string Path => _path;

        public static void Initialise()
        {
            try
            {
                var root = BepInEx.Paths.BepInExRootPath;
                _path = System.IO.Path.Combine(root, "LooseLips-transcript.log");
            }
            catch
            {
                _path = null;
            }
        }

        /// <summary>Start a fresh transcript, so each playtest is readable on its own.</summary>
        public static void BeginSession(string version)
        {
            if (!ModConfig.WriteTranscript.Value || _path == null) return;

            lock (Gate)
            {
                try
                {
                    var header = new StringBuilder();
                    header.AppendLine();
                    header.AppendLine(new string('=', 78));
                    header.AppendLine("Loose Lips " + version + " - session started " +
                                      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    header.AppendLine("Model endpoint: " + ModConfig.BaseUrl.Value + ModConfig.ChatPath.Value +
                                      "  model: " + (string.IsNullOrWhiteSpace(ModConfig.Model.Value)
                                          ? "(app default)" : ModConfig.Model.Value));
                    header.AppendLine(new string('=', 78));
                    File.AppendAllText(_path, header.ToString(), Encoding.UTF8);
                    _headerWritten = true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not open the transcript: " + e.Message);
                    _path = null;
                }
            }
        }

        /// <summary>One complete exchange, from what the citizen was told to what the game allowed.</summary>
        public static void Exchange(
            string citizenName,
            bool shouted,
            int earshotCount,
            string playerLine,
            long latencyMs,
            string rawReply,
            string spokenLine,
            float truthfulness,
            float alarm,
            string reasoning,
            IEnumerable<string> effectsApplied,
            IEnumerable<string> effectsRejected,
            string systemPrompt,
            string turnMessage)
        {
            if (!ModConfig.WriteTranscript.Value || _path == null) return;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("--- " + DateTime.Now.ToString("HH:mm:ss") + "  " + (citizenName ?? "?") +
                          "  [" + (shouted ? "SHOUTED" : "spoken") + ", " + earshotCount + " in earshot] ---");
            sb.AppendLine("YOU : " + Flatten(playerLine));

            if (spokenLine == null)
            {
                sb.AppendLine("THEM: (no usable reply after " + latencyMs + " ms)");
                if (!string.IsNullOrWhiteSpace(rawReply))
                    sb.AppendLine("raw : " + Clip(Flatten(rawReply), 500));
            }
            else
            {
                sb.AppendLine("THEM: " + Flatten(spokenLine));
                sb.AppendLine("      truthfulness " + truthfulness.ToString("0.00") +
                              "   alarm " + alarm.ToString("0.00") +
                              "   " + latencyMs + " ms");
                if (!string.IsNullOrWhiteSpace(reasoning))
                    sb.AppendLine("      thinking: " + Flatten(reasoning));
            }

            var applied = Join(effectsApplied);
            var rejected = Join(effectsRejected);
            sb.AppendLine("done: " + (applied.Length == 0 ? "nothing" : applied));
            if (rejected.Length > 0) sb.AppendLine("no  : " + rejected);

            if (ModConfig.TranscribePrompts.Value)
            {
                sb.AppendLine("  . . . what they were told . . .");
                sb.AppendLine(Indent(systemPrompt));
                sb.AppendLine(Indent(turnMessage));
            }

            Write(sb.ToString());
        }

        /// <summary>Free-form note, used by the self-test and by anything worth marking in place.</summary>
        public static void Note(string text)
        {
            if (!ModConfig.WriteTranscript.Value || _path == null) return;
            Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        }

        /// <summary>
        /// Roll the transcript over once it gets large.
        ///
        /// It is append-only and never deleted, so a long-running install would grow one file
        /// forever - and the thing anybody actually needs is the recent past, not the first
        /// conversation they ever had. One previous file is kept, which is enough to cover a
        /// crash that happens just after a roll.
        /// </summary>
        private static void RollIfLarge()
        {
            try
            {
                if (_path == null || !File.Exists(_path)) return;
                if (new FileInfo(_path).Length < 4L * 1024 * 1024) return;

                var previous = _path + ".1";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(_path, previous);

                Plugin.Log.LogInfo("Transcript reached 4 MB; the older half is now " +
                                   System.IO.Path.GetFileName(previous) + ".");
            }
            catch
            {
                // Not being able to roll is not a reason to stop writing.
            }
        }

        private static int _writesSinceCheck;

        private static void Write(string text)
        {
            lock (Gate)
            {
                try
                {
                    if (!_headerWritten) BeginSession("(session already running)");

                    // Checking the file size on every line would be its own small cost.
                    if (++_writesSinceCheck >= 50)
                    {
                        _writesSinceCheck = 0;
                        RollIfLarge();
                    }

                    File.AppendAllText(_path, text, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Transcript write failed: " + e.Message);
                    _path = null;   // stop trying; the BepInEx log still has the essentials
                }
            }
        }

        private static string Join(IEnumerable<string> items)
        {
            if (items == null) return "";
            var list = new List<string>();
            foreach (var i in items) if (!string.IsNullOrWhiteSpace(i)) list.Add(i);
            return string.Join("; ", list);
        }

        private static string Flatten(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("\r", " ").Replace("\n", " ").Trim();

        private static string Clip(string s, int n)
            => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "...";

        private static string Indent(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var lines = s.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            foreach (var l in lines) sb.AppendLine("      | " + l);
            return sb.ToString().TrimEnd();
        }
    }
}
