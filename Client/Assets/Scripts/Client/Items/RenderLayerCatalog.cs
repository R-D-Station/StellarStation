using System;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Items
{
    [CreateAssetMenu(menuName = "Station/Render Layer Catalog", fileName = "RenderLayerCatalog")]
    public sealed class RenderLayerCatalog : ScriptableObject
    {
        [Serializable]
        public struct LayerEntry
        {
            public ushort Id;
            public string Name;
            public int Order;
        }

        [SerializeField] private List<LayerEntry> _layers = new List<LayerEntry>();

        public IReadOnlyList<LayerEntry> Entries => _layers;

        public int OrderOf(ushort id)
        {
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i].Id == id) return _layers[i].Order;
            return 0;
        }

        public string NameOf(ushort id)
        {
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i].Id == id) return _layers[i].Name;
            return id == 0 ? "" : $"#{id}";
        }

        public bool TryFind(string name, out ushort id)
        {
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i].Name == name) { id = _layers[i].Id; return true; }
            id = 0;
            return false;
        }

        public ushort NextFreeId()
        {
            ushort max = 0;
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i].Id > max) max = _layers[i].Id;
            return (ushort)(max + 1);
        }

        public ushort AddLayer(string name, int order)
        {
            ushort id = NextFreeId();
            _layers.Add(new LayerEntry { Id = id, Name = name, Order = order });
            return id;
        }
    }
}
