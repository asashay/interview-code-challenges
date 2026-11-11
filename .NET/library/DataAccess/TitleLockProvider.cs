using System.Collections.Concurrent;

namespace OneBeyondApi.DataAccess
{
    // Registered as a singleton. Retain entries so waiters always share one lock.
    public class TitleLockProvider
    {
        private readonly ConcurrentDictionary<Guid, object> _locks = new();

        public object GetLock(Guid bookId) => _locks.GetOrAdd(bookId, _ => new object());
    }
}
