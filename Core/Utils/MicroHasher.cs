namespace OpenCrossoutProtocol;
public class CacheItem
{
    public string Value { get; }
    public int Length { get; }
    public uint Hash { get; }
    public int RefCount;

    public CacheItem Next;

    public CacheItem(string value, int length, uint hash)
    {
        Value = value;
        Length = length;
        Hash = hash;
        RefCount = 1;
    }
}

public static class Hasher
{
    private static readonly object _syncRoot = new object();
    private static List<CacheItem>[] _buckets = new List<CacheItem>[64];
    private static int _globalCounter;


    private sealed class ItemComparerCore : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            uint hashX = CalculateFNV1AHash(x);
            uint hashY = CalculateFNV1AHash(y);
            return hashX.CompareTo(hashY);
        }
    }
    /// <summary>
    /// Для использования MicroHasher в контексте OrderBy(var, Hasher.ItemComparer)
    /// </summary>
    public static IComparer<string> ItemComparer { get; } = new ItemComparerCore();

    static Hasher()
    {
        for (int i = 0; i < _buckets.Length; i++)
            _buckets[i] = new List<CacheItem>();
    }

    /// <summary>
    /// Сортирует строки в правильном порядке для хорошего восприятия в игре
    /// </summary>
    public static string[] SortItems(string[] items)
    {
        return items
                .Select(s => (Hash: InsertOrAcquire(40, s).Hash, Original: s))
                .OrderBy(x => x.Hash)
                .Select(x => x.Original)
                .ToArray();
    }

    private static CacheItem InsertOrAcquire(int ctr, string itemName)
    {
        if (itemName == null)
        {
            Interlocked.Increment(ref _globalCounter);
            return new CacheItem(null, 0, 0) { RefCount = 0 };
        }

        uint hash = CalculateFNV1AHash(itemName);
        int length = itemName.Length;

        lock (_syncRoot)
        {
            int bucketIndex = (int)(hash % (uint)_buckets.Length);
            var bucket = _buckets[bucketIndex];
            foreach (var item in bucket)
            {
                if (item.Hash == hash && item.Length == length && item.Value == itemName)
                {
                    Interlocked.Increment(ref item.RefCount);
                    return item;
                }
            }
            var newItem = new CacheItem(itemName, length, hash);
            newItem.Next = bucket.Count > 0 ? bucket[0] : null;
            bucket.Insert(0, newItem);
            return newItem;
        }
    }

    public static void Release(CacheItem item)
    {
        if (item == null || item.RefCount <= 0) return;

        int newCount = Interlocked.Decrement(ref item.RefCount);
        if (newCount == 0)
        {
            lock (_syncRoot)
            {
                int bucketIndex = (int)(item.Hash % (uint)_buckets.Length);
                _buckets[bucketIndex].Remove(item);
            }
        }
    }

    private static uint CalculateFNV1AHash(string input)
    {
        const uint prime = 0x01000193;
        uint hash = 0x811C9DC5;

        foreach (char c in input)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }
}