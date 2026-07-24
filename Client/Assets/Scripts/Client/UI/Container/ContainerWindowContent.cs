using UnityEngine;
using UnityEngine.UI;
using Shared.Messages.Interaction;
using Client.Items;
using Client.Net;
using Client.UI.Windows;

namespace Client.UI.Container
{
    /// <summary>Содержимое окна контейнера — слоты + кнопка «положить», применяет ContainerSync.</summary>
    public sealed class ContainerWindowContent : MonoBehaviour
    {
        [SerializeField] private ContainerSlotHud[] _slots;
        [SerializeField] private Button _putButton;

        private int _netId = -1;
        private NetworkRunner _runner;
        private ItemCatalog _catalog;
        private int _slotCount;

        private void Awake()
        {
            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (slot == null) continue;
                    slot.Index = i;
                    slot.Clicked += OnSlotClicked;
                }
            if (_putButton != null) _putButton.onClick.AddListener(OnPutClicked);
        }

        private void OnDestroy()
        {
            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i] != null) _slots[i].Clicked -= OnSlotClicked;
            if (_putButton != null) _putButton.onClick.RemoveListener(OnPutClicked);
        }

        /// <summary>Привязывает окно к контейнеру: сеть, каталог, заголовок и число активных слотов.</summary>
        public void Bind(int netId, string title, NetworkRunner runner, ItemCatalog catalog, int slotCount)
        {
            _netId = netId;
            _runner = runner;
            _catalog = catalog;
            _slotCount = slotCount;

            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i] != null) _slots[i].gameObject.SetActive(i < slotCount);

            if (_slots != null && slotCount > _slots.Length)
                Debug.LogWarning($"[ContainerWindowContent] в префабе окна {_slots.Length} слотов < запрошенных {slotCount}");

            var window = GetComponent<UiWindow>();
            if (window != null) window.SetTitle(title);
        }

        /// <summary>Перерисовывает слоты из серверного снимка содержимого.</summary>
        public void Apply(in ContainerSync sync)
        {
            if (_slots == null) return;

            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null) _slots[i].SetEmpty();

            var items = sync.Items;
            if (items == null) return;

            int visible = Mathf.Min(_slotCount, _slots.Length);
            int n = Mathf.Min(items.Length, visible);
            for (int i = 0; i < n; i++)
                if (_slots[i] != null) _slots[i].SetFilled(items[i].ItemDefId, items[i].StackCount, _catalog);

            if (items.Length > visible)
                Debug.LogWarning($"[ContainerWindowContent] контейнер {_netId}: предметов {items.Length} > видимых слотов {visible}, лишние не показаны");
        }

        private void OnSlotClicked(int index)
        {
            if (_runner != null) _runner.SendTakeFromContainer(_netId, (ushort)index);
        }

        private void OnPutClicked()
        {
            if (_runner != null) _runner.SendPutInContainer(_netId);
        }
    }
}
