using System;
using System.Collections.Concurrent;
using LooseLips.Core;

namespace LooseLips.Core
{
    /// <summary>
    /// Unity's API is single-threaded. Network work happens on the thread pool, so every
    /// result that touches the game has to come back through here and be drained from a
    /// component running on the main thread.
    /// </summary>
    public static class MainThread
    {
        private static readonly ConcurrentQueue<Action> Pending = new ConcurrentQueue<Action>();

        /// <summary>Queue work to run on the next main-thread frame.</summary>
        public static void Post(Action action)
        {
            if (action != null) Pending.Enqueue(action);
        }

        /// <summary>Drain the queue. Call this once per frame from a MonoBehaviour Update.</summary>
        public static void Drain()
        {
            while (Pending.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Queued main-thread action threw: {e}");
                }
            }
        }
    }
}
