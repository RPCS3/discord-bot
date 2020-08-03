using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CompatBot.Utils
{
	public class UniqueList<T>: IList<T>
	{
		private readonly List<T> list;
		private readonly HashSet<T> set;

		public UniqueList(IEqualityComparer<T> comparer = null)
		{
			list = new List<T>();
			set = new HashSet<T>(comparer);
			this.Comparer = comparer;
		}

		public UniqueList(int count, IEqualityComparer<T> comparer = null)
		{
			list = new List<T>(count);
			set = new HashSet<T>(count, comparer);
			this.Comparer = comparer;
		}

		public UniqueList(IEnumerable<T> collection, IEqualityComparer<T> comparer = null)
		{
			this.Comparer = comparer;
			if (collection is ICollection c)
			{
				list = new List<T>(c.Count);
				set = new HashSet<T>(c.Count, comparer);
			}
			else
			{
				list = new List<T>();
				set = new HashSet<T>(comparer);
			}
			foreach (var item in collection)
				if (set.Add(item))
					list.Add(item);
		}

		public IEnumerator<T> GetEnumerator() => list.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)list).GetEnumerator();

		public void Add(T item)
		{
			if (set.Add(item))
				list.Add(item);
		}

		public void AddRange(IEnumerable<T> collection)
		{
			foreach (var item in collection)
				Add(item);
		}

		public void Clear()
		{
			list.Clear();
			set.Clear();
		}

		public bool Contains(T item) => set.Contains(item);

		public void CopyTo(T[] array, int arrayIndex) => list.CopyTo(array, arrayIndex);

		public bool Remove(T item)
		{
			list.Remove(item);
			return set.Remove(item);
		}

		public int Count => list.Count;

		public bool IsReadOnly => false;

		public int IndexOf(T item) => list.IndexOf(item);

		public void Insert(int index, T item)
		{
			if (set.Add(item))
				list.Insert(index, item);
			else if (IndexOf(item) != index)
				throw new ArgumentException("Collection already contains item at different index", nameof(item));
		}

		public void RemoveAt(int index)
		{
			var item = this[index];
			list.RemoveAt(index);
			set.Remove(item);
		}

		public T this[int index] {
			get => list[index];
			set => throw new NotSupportedException();
		}

		public IEnumerable<T> this[Range range]
		{
			get
			{
				var (offset, count) = range.GetOffsetAndLength(list.Count);
				return list.Skip(offset).Take(count);
			}
		}

		public int Length => list.Count;
		public IEqualityComparer<T> Comparer { get; }

		T GetAt(int index) => list[index];
	}
}
