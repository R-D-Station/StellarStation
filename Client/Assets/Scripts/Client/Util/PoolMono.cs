using System;
using System.Collections.Generic;
using UnityEngine;
using Client.UI.Labels;

namespace Client.Util
{
    /// <summary>Пул MonoBehaviour-объектов (порт внешнего PoolMono, адаптирован). Свободная = !activeInHierarchy;
    /// O(n) free-scan (надписей единицы). При выдаче дёргает <see cref="IPooledLabel.OnAcquire"/> (reset до pristine) —
    /// PoolMono остаётся ОБЩИМ (хук опционален через is-проверку). autoExpand БЕЗ hard-cap (в отличие от тайл-пула 512).</summary>
    public class PoolMono<T> where T : MonoBehaviour
    {
        public T prefab { get; }
        public Transform container { get; }
        public bool autoExpand { get; set; }

        private readonly List<T> _pool;

        public PoolMono(T prefab, int count) : this(prefab, count, null) { }

        public PoolMono(T prefab, int count, Transform container)
        {
            this.prefab = prefab;
            this.container = container;
            _pool = new List<T>();
            CreatePool(count);
        }

        private void CreatePool(int count)
        {
            for (int i = 0; i < count; i++)
                CreateObject();
        }

        private T CreateObject(Vector3 position = default, bool isActiveByDefault = false)
        {
            var created = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, container);
            created.gameObject.SetActive(isActiveByDefault);
            _pool.Add(created);
            return created;
        }

        public bool HasFreeElement(out T element)
        {
            foreach (var mono in _pool)
            {
                if (!mono.gameObject.activeInHierarchy)
                {
                    element = mono;
                    mono.gameObject.SetActive(true);
                    return true;
                }
            }
            element = null;
            return false;
        }

        public bool HasFreeElement(out T element, Vector3 position)
        {
            if (HasFreeElement(out element))
            {
                element.transform.position = position;
                return true;
            }
            return false;
        }

        public T GetFreeElement(Vector3 position = default)
        {
            if (HasFreeElement(out var element, position))
            {
                Acquire(element);
                return element;
            }
            if (autoExpand)
            {
                var created = CreateObject(position, true);
                Acquire(created);
                return created;
            }
            throw new Exception($"PoolMono<{typeof(T).Name}>: нет свободного элемента и autoExpand=false");
        }

        // Reset-хук при выдаче: если элемент реализует IPooledLabel — сбросить до pristine (для НЕ-надписей — no-op).
        private static void Acquire(T element)
        {
            if (element is IPooledLabel p) p.OnAcquire();
        }

        public void SetNotActive(T element) => element.gameObject.SetActive(false);

        public void AddInPool(T element) => _pool.Add(element);

        public T AddElement(bool isActiveByDefault = false) => CreateObject(default, isActiveByDefault);

        public List<T> GetPool() => _pool;
    }
}
