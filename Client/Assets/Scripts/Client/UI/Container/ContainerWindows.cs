using System.Collections.Generic;
using UnityEngine;
using Shared.Messages.Interaction;
using Client.Items;
using Client.Net;
using Client.UI.Windows;

namespace Client.UI.Container
{
    /// <summary>Сервис сцены: владеет протоколом клиентских окон контейнеров (открытие/закрытие/синк/дальность).</summary>
    public sealed class ContainerWindows : MonoBehaviour
    {
        [SerializeField] private UiWindowManager _manager;
        [Tooltip("Базовое окно контейнера; предмет может переопределить своим ItemDefinition.WindowPrefab.")]
        [SerializeField] private UiWindow _defaultWindowPrefab;
        [SerializeField] private ItemCatalog _catalog;
        [SerializeField] private NetworkRunner _runner;

        private readonly Dictionary<int, UiWindow> _windows = new Dictionary<int, UiWindow>();
        private readonly Dictionary<int, ContainerWindowContent> _contents = new Dictionary<int, ContainerWindowContent>();
        private readonly List<int> _closeScratch = new List<int>();
        private Vector2? _lastClosedPos;

        public bool IsOpen(int netId) => _windows.ContainsKey(netId);

        // авто-закрытие открытых окон, если контейнер вышел из досягаемости (сервер больше не шлёт ContainerSync)
        private void Update()
        {
            if (_runner == null || _windows.Count == 0) return;
            _closeScratch.Clear();
            foreach (var kv in _windows)
                if (!_runner.IsGroundContainerReachable(kv.Key))
                    _closeScratch.Add(kv.Key);
            for (int i = 0; i < _closeScratch.Count; i++)
                Close(_closeScratch[i]);
        }

        /// <summary>Открывает окно контейнера (или закрывает, если уже открыто); шлёт запрос серверу.</summary>
        public void Toggle(int netId, ushort defId)
        {
            if (IsOpen(netId)) { Close(netId); return; }

            var def = _catalog?.For(defId);
            var prefab = def != null && def.WindowPrefab != null ? def.WindowPrefab : _defaultWindowPrefab;
            if (_manager == null || prefab == null || _runner == null) return;

            _runner.SendOpenContainer(netId);

            var w = _manager.Open(prefab);
            if (w == null) return;

            if (_lastClosedPos.HasValue && w.transform is RectTransform rt)
                rt.anchoredPosition = _lastClosedPos.Value + new Vector2(24f, -24f) * _windows.Count;

            var c = w.GetComponent<ContainerWindowContent>();
            if (c == null) { _manager.Close(w); return; }

            int slots = def != null ? Mathf.Clamp(def.MaxContents, 4, 16) : 16; // 4..16 — держит окно в разумных пределах вне зависимости от данных предмета
            c.Bind(netId, def?.DisplayName ?? "", _runner, _catalog, slots);
            w.CloseRequested += _ => Close(netId);

            _windows[netId] = w;
            _contents[netId] = c;
        }

        /// <summary>Единственный путь закрытия окна (крестик/повторный E/деспавн предмета) — уничтожает окно и уведомляет сервер.</summary>
        public void Close(int netId)
        {
            if (!_windows.TryGetValue(netId, out var w)) return;
            _windows.Remove(netId);
            _contents.Remove(netId);
            if (w != null && w.transform is RectTransform rt)
                _lastClosedPos = rt.anchoredPosition;
            _manager?.Close(w);
            _runner?.SendCloseContainer(netId);
        }

        /// <summary>Маршрутизирует ContainerSync в открытое окно по NetId контейнера.</summary>
        public void OnSync(in ContainerSync sync)
        {
            if (_contents.TryGetValue(sync.ContainerNetId, out var c) && c != null)
                c.Apply(in sync);
        }

        /// <summary>Закрывает окно, если контейнер-предмет исчез из мира (деспавн/подбор).</summary>
        public void OnItemGone(int netId)
        {
            if (IsOpen(netId)) Close(netId);
        }
    }
}
