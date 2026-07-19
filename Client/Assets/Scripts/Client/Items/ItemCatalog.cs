using UnityEngine;

namespace Client.Items
{
    /// <summary>Каталог предметов: резолв ItemDefinition по ItemDefId (линейный — не горячий путь, резолв при спавне вью).</summary>
    [CreateAssetMenu(menuName = "Station/Item Catalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private ItemDefinition[] _definitions = System.Array.Empty<ItemDefinition>();

        /// <summary>ItemDefinition по id; null — нет в каталоге.</summary>
        public ItemDefinition For(ushort defId)
        {
            for (int i = 0; i < _definitions.Length; i++)
                if (_definitions[i] != null && _definitions[i].ItemDefId == defId)
                    return _definitions[i];
            return null;
        }
    }
}
