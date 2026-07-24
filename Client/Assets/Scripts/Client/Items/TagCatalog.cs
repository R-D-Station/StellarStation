using System;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Items
{
    /// <summary>Реестр тегов предмета: стабильные id (не перенумеровывать), новые — через NextFreeId/AddTag.</summary>
    [CreateAssetMenu(menuName = "Station/Tag Catalog", fileName = "TagCatalog")]
    public sealed class TagCatalog : ScriptableObject
    {
        [Serializable]
        public struct TagEntry
        {
            public ushort Id;
            public string Name;
        }

        [SerializeField] private List<TagEntry> _tags = new List<TagEntry>();

        public IReadOnlyList<TagEntry> Entries => _tags;

        public string NameOf(ushort id)
        {
            for (int i = 0; i < _tags.Count; i++)
                if (_tags[i].Id == id) return _tags[i].Name;
            return id == 0 ? "" : $"#{id}";
        }

        public bool TryFind(string name, out ushort id)
        {
            for (int i = 0; i < _tags.Count; i++)
                if (_tags[i].Name == name) { id = _tags[i].Id; return true; }
            id = 0;
            return false;
        }

        public ushort NextFreeId()
        {
            ushort max = 0;
            for (int i = 0; i < _tags.Count; i++)
                if (_tags[i].Id > max) max = _tags[i].Id;
            return (ushort)(max + 1);
        }

        public ushort AddTag(string name)
        {
            ushort id = NextFreeId();
            _tags.Add(new TagEntry { Id = id, Name = name });
            return id;
        }
    }
}
