using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CompatBot.Utils;

internal class FixedLengthBuffer<TKey, TValue>: IList<TValue>
    where TKey: notnull
{
    internal readonly object SyncObj = new();
        
    public FixedLengthBuffer(Func<TValue, TKey> keyGenerator)
    {
        makeKey = keyGenerator;
    }

    public FixedLengthBuffer<TKey, TValue> CloneShallow()
    {
        var result = new FixedLengthBuffer<TKey, TValue>(makeKey);
        foreach (var key in keyList)
            result.keyList.Add(key);
        foreach (var (key, value) in lookup)
            result.lookup[key] = value;
        return result;
    }

    public IEnumerator<TValue> GetEnumerator() => keyList.Select(k => lookup[k]).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(TValue item)
    {
        var key = makeKey(item);
        if (!lookup.ContainsKey(key))
            keyList.Add(key);
        lookup[key] = item;
    }

    public void AddOrReplace(TValue item) => Add(item);

    public void Clear()
    {
        keyList.Clear();
        lookup.Clear();
    }

    public void TrimOldItems(int count)
    {
        var keys = keyList.Take(count).ToList();
        keyList.RemoveRange(0, keys.Count);
        foreach (var k in keys)
            lookup.Remove(k);
    }

    public void TrimExcess()
    {
        if (Count <= FixedLengthBuffer<TKey, TValue>.MaxLength)
            return;

        TrimOldItems(Count - FixedLengthBuffer<TKey, TValue>.MaxLength);
    }

    public List<TValue> GetOldItems(int count)
        => keyList.Take(Math.Min(Count, count)).Select(k => lookup[k]).ToList();

    public List<TValue> GetExcess()
    {
        if (Count <= FixedLengthBuffer<TKey, TValue>.MaxLength)
            return new List<TValue>(0);
        return GetOldItems(Count - FixedLengthBuffer<TKey, TValue>.MaxLength);
    }

    public TValue? Evict(TKey key)
    {
        if (!lookup.TryGetValue(key, out var result))
            return result;
            
        lookup.Remove(key);
        keyList.Remove(key);
        return result;
    }

    public bool Remove(TValue item)
    {
        var key = makeKey(item);
        if (!Contains(key))
            return false;
            
        Evict(key);
        return true;
    }

    public void CopyTo(TValue[] array, int arrayIndex)
    {
        var available = array.Length-arrayIndex;
        if (available < Count)
            throw new ArgumentException($"Insufficient lenght of the destination array: available={available}, required={Count}");

        for (var i = 0; i < Count; i++)
            array[arrayIndex + i] = lookup[keyList[i]];
    }

    public bool Contains(TKey key) => lookup.ContainsKey(key);
    public bool Contains(TValue item) => Contains(makeKey(item));
        
    public int IndexOf(TValue item) => throw new NotSupportedException();
    public void Insert(int index, TValue item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();

    public TValue this[int index]
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public int Count => lookup.Count;
    public bool IsReadOnly => false;

    public bool NeedTrimming => Count > FixedLengthBuffer<TKey, TValue>.MaxLength + 20;
    public TValue this[TKey index] => lookup[index];

    private static int MaxLength => Config.ChannelMessageHistorySize;
    private readonly Func<TValue, TKey> makeKey;
    private readonly List<TKey> keyList = new();
    private readonly Dictionary<TKey, TValue> lookup = new();
}