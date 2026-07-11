using System.Collections.Generic;
using UnityEngine;

namespace Client.Map
{
    /// <summary>Рантайм-поиск BlockDefinition по id (визуал: префабы/спрайты; данные сим — в BlockCatalog).
    /// В игре — скан Resources (ассеты блоков класть в любую папку Resources, напр. Assets/Resources/Blocks);
    /// в редакторе — фолбэк на AssetDatabase (найдёт и вне Resources, но предупредит: билд их не увидит).</summary>
    public static class BlockDefinitionResolver
    {
        private static Dictionary<ushort, BlockDefinition> _byId;

        public static BlockDefinition Find(ushort type)
        {
            if (_byId == null)
                Build();
            return _byId.TryGetValue(type, out var d) ? d : null;
        }

        /// <summary>Сброс кэша (редактор: после создания/переноса ассетов, кодогена, загрузки карты).</summary>
        public static void Invalidate() => _byId = null;

        private static void Build()
        {
            _byId = new Dictionary<ushort, BlockDefinition>();
            foreach (var def in Resources.LoadAll<BlockDefinition>(""))
                if (def.Type != 0)
                    _byId[def.Type] = def;

#if UNITY_EDITOR
            // Edit-mode: доискиваем вне Resources, чтобы редактор карт видел все ассеты — но такие
            // дефиниции в ИГРЕ резолвиться не будут, предупреждаем по разу.
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:BlockDefinition"))
            {
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<BlockDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.Type == 0 || _byId.ContainsKey(def.Type))
                    continue;
                _byId[def.Type] = def;
                Debug.LogWarning($"[BlockDefinitionResolver] '{def.name}' (id {def.Type}) вне папки Resources — " +
                                 "в редакторе виден, в игре префаб НЕ зарезолвится. Перенеси ассет в Resources.", def);
            }
#endif
        }
    }
}
