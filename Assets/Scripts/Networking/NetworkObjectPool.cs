using System;
using System.Collections.Generic;

namespace Networking
{
    // TODO : Optimize internal data structures
    public class NetworkObjectPool<T> where T : class, new()
    {
        private readonly Func<T> _factory;
        private readonly List<T> _free = new List<T>();
        private readonly List<T> _allocated = new List<T>();

        public int allocated => _allocated.Count;

        public int capacity => _allocated.Capacity;

        public NetworkObjectPool(int initialSize, Func<T> factory = null)
        {
            Grow(initialSize);
            _factory = factory;
        }

        public T Allocate()
        {
            if (_free.Count == 0)
            {
                Grow(_free.Capacity * 2);
            }

            var element = _free[_free.Count - 1];
            _free.RemoveAt(_free.Count - 1);
            _allocated.Add(element);
            return element;
        }

        public void Release(T t)
        {
            bool result = _allocated.Remove(t);
            GameDebug.Assert(result);
            _free.Add(t);
        }

        public void Reset()
        {
            foreach (var item in _allocated)
            {
                _free.Add(item);
            }

            _allocated.Clear();
        }

        private void Grow(int count)
        {
            _free.Capacity += count;
            _allocated.Capacity += count;

            for (var i = 0; i < count; ++i)
            {
                _free.Add(_factory != null ? _factory() : new T());
            }
        }
    }
}