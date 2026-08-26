using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Mot thong bao trong history.
    /// </summary>
    public class NotificationItem
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public string Account { get; init; } = "";
        public string Type { get; init; } = "";      // BossFound / BossKilled / BotError / Safety
        public string Message { get; init; } = "";
        public bool HighPriority { get; init; }

        public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Notification manager (task 18 cua spec):
    /// - Rate limit toi da 10 thong bao / phut / bot (18.1)
    /// - History 50 muc gan nhat (18.5)
    /// - UI dang ky event OnNotification de hien toast + tray balloon
    /// Thread-safe (events den tu socket thread).
    /// </summary>
    public class NotificationManager
    {
        private const int MaxPerMinutePerBot = 10;
        private const int HistoryLimit = 50;

        public class Notification
        {
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string Account { get; init; } = "";
            public string Type { get; init; } = "";
            public string Message { get; init; } = "";
            public bool HighPriority { get; init; }

            public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
        }

        private readonly object _lock = new();
        private readonly Queue<Notification> _history = new();
        private readonly Dictionary<string, Queue<DateTime>> _rateBuckets = new();

        public sealed class RateLimitedEventArgs : EventArgs
        {
            public Notification Item { get; init; } = null!;
        }

        /// <summary>Chi fire khi QUOTA cho phep. UI hien toast/balloon tu day.</summary>
        public event EventHandler<Notification>? OnNotificationAllowed;

        public IReadOnlyList<Notification> History
        {
            get { lock (_lock) return _history.ToList(); }
        }

        /// <summary>Raise mot notification (tu dong rate-limit). Tra ve false neu bi limit.</summary>
        public bool Raise(string account, string type, string message, bool highPriority)
        {
            var item = new Notification { Account = account, Type = type, Message = message, HighPriority = highPriority };

            lock (_lock)
            {
                // Luu history truoc - history khong anh huong rate limit
                _history.Enqueue(item);
                while (_history.Count > HistoryLimit) _history.Dequeue();

                var now = DateTime.Now;
                if (!_rateBuckets.TryGetValue(account, out var bucket))
                {
                    bucket = new Queue<DateTime>();
                    _rateBuckets[account] = bucket;
                }
                while (bucket.Count > 0 && (now - bucket.Peek()).TotalSeconds >= 60)
                    bucket.Dequeue();

                if (bucket.Count >= MaxPerMinutePerBot)
                {
                    return false; // RATE LIMITED (REQ 19.7)
                }
                bucket.Enqueue(now);
            }

            OnNotificationAllowed?.Invoke(this, item);
            return true;
        }
    }
}
