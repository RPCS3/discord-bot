using System;
using System.Collections;
using System.Collections.Generic;

namespace CompatBot.Utils
{
	public class NameUniqueObjectCollection<TValue>: IDictionary<string, UniqueList<TValue>>
	{
		private readonly Dictionary<string, UniqueList<TValue>> dict;
		private readonly IEqualityComparer<TValue> valueComparer;

		public NameUniqueObjectCollection(IEqualityComparer<string> keyComparer = null, IEqualityComparer<TValue> valueComparer = null)
		{
			dict = new Dictionary<string, UniqueList<TValue>>(keyComparer);
			this.valueComparer = valueComparer;
		}

		public IEnumerator<KeyValuePair<string, UniqueList<TValue>>> GetEnumerator() => dict.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)dict).GetEnumerator();

		void ICollection<KeyValuePair<string, UniqueList<TValue>>>.Add(KeyValuePair<string, UniqueList<TValue>> item) => Add(item.Key, item.Value);

		public void Add(string key, UniqueList<TValue> value)
		{
			value ??= new UniqueList<TValue>(valueComparer);
			if (dict.TryGetValue(key, out var c))
				c.AddRange(value);
			else
				dict.Add(key, value);
		}

		public void Add(string key, TValue value)
		{
			if (dict.TryGetValue(key, out var c))
				c.Add(value);
			else
			{
				dict[key] = c = new UniqueList<TValue>(valueComparer);
				c.Add(value);
			}
		}

		public void Clear() => dict.Clear();

		bool ICollection<KeyValuePair<string, UniqueList<TValue>>>.Contains(KeyValuePair<string, UniqueList<TValue>> item) => ((IDictionary<string, UniqueList<TValue>>)dict).Contains(item);

		void ICollection<KeyValuePair<string, UniqueList<TValue>>>.CopyTo(KeyValuePair<string, UniqueList<TValue>>[] array, int arrayIndex) => ((IDictionary<string, UniqueList<TValue>>)dict).CopyTo(array, arrayIndex);

		bool ICollection<KeyValuePair<string, UniqueList<TValue>>>.Remove(KeyValuePair<string, UniqueList<TValue>> item) => ((IDictionary<string, UniqueList<TValue>>)dict).Remove(item);

		public int Count => dict.Count;
		public bool IsReadOnly => false;

		public bool ContainsKey(string key) => dict.ContainsKey(key);

		public bool Remove(string key) => dict.Remove(key);

		public bool TryGetValue(string key, out UniqueList<TValue> value)
		{
			var result = dict.TryGetValue(key, out value);
			if (!result)
				dict[key] = value = new UniqueList<TValue>(valueComparer);
			return result;
		}

		public UniqueList<TValue> this[string key]
		{
			get
			{
				TryGetValue(key, out var value);
				return value;
			}
			set => dict[key] = (value ?? new UniqueList<TValue>(valueComparer));
		}

		public ICollection<string> Keys => dict.Keys;
		public ICollection<UniqueList<TValue>> Values => dict.Values;
	}
}