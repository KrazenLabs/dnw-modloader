using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;

namespace DnWModLoader.Config
{
    internal static class DeferredSaves
    {
        private sealed class Pending
        {
            public DateTime DueAt;
            public Action Save;
        }

        private static readonly Dictionary<object, Pending> Scheduled = new Dictionary<object, Pending>();

        public static void Schedule(object owner, Action save)
        {
            lock (Scheduled) Scheduled[owner] = new Pending { DueAt = DateTime.UtcNow + ModConfig.SaveDelay, Save = save };
        }

        public static void Cancel(object owner)
        {
            lock (Scheduled) Scheduled.Remove(owner);
        }

        public static void FlushDue()
        {
            if (Scheduled.Count == 0) return;
            var now = DateTime.UtcNow;
            Run(pending => now >= pending.DueAt);
        }

        public static void FlushAll()
        {
            Run(pending => true);
        }

        private static void Run(Func<Pending, bool> isDue)
        {
            KeyValuePair<object, Pending>[] due;
            lock (Scheduled)
            {
                due = Scheduled.Where(pair => isDue(pair.Value)).ToArray();
                foreach (var pair in due) Scheduled.Remove(pair.Key);
            }
            Exception first = null;
            foreach (var pair in due)
            {
                try { pair.Value.Save(); }
                catch (Exception e) { if (first == null) first = e; }
            }
            if (first != null) ExceptionDispatchInfo.Capture(first).Throw();
        }
    }
}
