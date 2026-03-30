using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModUtils
{
    public class InstanceCache<T> : MonoBehaviour
    {
        private static readonly HashSet<T> Cache;

        private T _instance;

        static InstanceCache()
        {
            Cache = new HashSet<T>();
        }

        public static Action<T> OnCacheAdded { get; set; }
        public static Action<T> OnCacheRemoved { get; set; }

        private void Awake()
        {
            _instance = GetInstance();
            if (_instance == null || (_instance is Object obj && !obj))
                throw new NullReferenceException("Failed to acquire instance.");
            Cache.Add(_instance);
            OnCacheAdded?.Invoke(_instance);
        }

        private void OnDestroy()
        {
            Cache.Remove(_instance);
            OnCacheRemoved?.Invoke(_instance);
            _instance = default;
        }

        protected virtual T GetInstance()
        {
            return GetComponent<T>();
        }

        public static IEnumerable<T> GetAllInstance()
        {
            return Cache.ToList();
        }

        public static void Fill(List<T> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            buffer.AddRange(Cache);
        }
    }
}
