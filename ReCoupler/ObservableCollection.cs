using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ObservableCollection
{
    public class ObservableCollection<T> : IList<T>, ICollection<T>, IEnumerable<T>//, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public event CollectionChangedEventDelegate CollectionChanged;
        public delegate void CollectionChangedEventDelegate(object sender, EventArgs e);
        private Collection<T> collection;

        private void notify(EventArgs e)
        {
            if (CollectionChanged != null && CollectionChanged.GetInvocationList().Any())
                CollectionChanged(this, e);
        }

        public ObservableCollection()
        {
            collection = new Collection<T>();
        }

        public ObservableCollection(IList<T> list)
        {
            collection = new Collection<T>(list);
        }

        public T this[int i]
        {
            get { return collection[i]; }
            set { collection[i] = value; }
        }

        public void RemoveAt(int index)
        {
            collection.RemoveAt(index);
            notify(EventArgs.Empty);
        }

        public int Count
        {
            get
            {
                return collection.Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return false;
            }
        }

        public void Add(T item)
        {
            collection.Add(item);
            notify(EventArgs.Empty);
        }

        public void Clear()
        {
            collection.Clear();
            notify(EventArgs.Empty);
        }

        public bool Contains(T item)
        {
            return collection.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            collection.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            bool result = collection.Remove(item);
            if (result)
                notify(EventArgs.Empty);
            return result;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return collection.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return collection.GetEnumerator();
        }

        public int IndexOf(T item)
        {
            return collection.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            collection.Insert(index, item);
            notify(EventArgs.Empty);
        }
    }
}
