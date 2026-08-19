using System.Collections.Generic;
using LooseLips.Player2;

namespace LooseLips.Core
{
    /// <summary>
    /// Per-citizen conversation history, keyed by citizen id. Kept in memory only:
    /// it is deliberately not saved, so loading an old save never resurrects a
    /// conversation the world no longer reflects.
    /// </summary>
    public static class ConversationMemory
    {
        private static readonly Dictionary<int, List<ChatMessage>> Threads = new Dictionary<int, List<ChatMessage>>();

        public static IReadOnlyList<ChatMessage> Get(int citizenId)
        {
            List<ChatMessage> thread;
            return Threads.TryGetValue(citizenId, out thread) ? thread : new List<ChatMessage>();
        }

        public static void Record(int citizenId, string playerLine, string citizenLine)
        {
            List<ChatMessage> thread;
            if (!Threads.TryGetValue(citizenId, out thread))
            {
                thread = new List<ChatMessage>();
                Threads[citizenId] = thread;
            }

            if (!string.IsNullOrWhiteSpace(playerLine)) thread.Add(ChatMessage.User(playerLine));
            if (!string.IsNullOrWhiteSpace(citizenLine)) thread.Add(ChatMessage.Assistant(citizenLine));

            // Two entries per exchange, so the cap is expressed in turns.
            var max = ModConfig.HistoryTurnsPerCitizen.Value * 2;
            if (max <= 0)
            {
                thread.Clear();
                return;
            }
            while (thread.Count > max) thread.RemoveAt(0);
        }

        public static void Forget(int citizenId) => Threads.Remove(citizenId);

        public static void Clear() => Threads.Clear();
    }
}
