#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Client.Map;
using Shared.World;

namespace Client.Editor.Inspectors
{
    /// <summary>Editor-мост id объектов: скан ВСЕХ ассетов вида через AssetDatabase → чистый
    /// <see cref="TileIdAllocator"/>. Полы и структуры — раздельные пространства id (скан по своему виду).
    /// Скан по диску (не по каталогу: в каталоге не все ассеты).</summary>
    internal static class TileIdEditorUtil
    {
        /// <summary>Наименьший свободный StructureType (1..255) среди всех StructureDefinition, кроме self; 0 = все заняты.</summary>
        public static byte SmallestFreeStructureId(StructureDefinition self)
            => TileIdAllocator.SmallestFreeId(UsedIds(LoadAll<StructureDefinition>(), self, s => s.Type));

        /// <summary>Наименьший свободный FloorType (1..255) среди всех FloorDefinition, кроме self; 0 = все заняты.</summary>
        public static byte SmallestFreeFloorId(FloorDefinition self)
            => TileIdAllocator.SmallestFreeId(UsedIds(LoadAll<FloorDefinition>(), self, f => f.Type));

        /// <summary>Другой StructureDefinition с тем же ненулевым Type (или null, если конфликта нет).</summary>
        public static StructureDefinition FindStructureIdConflict(StructureDefinition self)
            => FindConflict(LoadAll<StructureDefinition>(), self, s => s.Type);

        /// <summary>Другой FloorDefinition с тем же ненулевым Type (или null, если конфликта нет).</summary>
        public static FloorDefinition FindFloorIdConflict(FloorDefinition self)
            => FindConflict(LoadAll<FloorDefinition>(), self, f => f.Type);

        // Id всех ассетов вида, кроме self и кроме Type==0 (не назначен).
        private static IEnumerable<byte> UsedIds<T>(IEnumerable<T> all, T self, System.Func<T, byte> id) where T : Object
        {
            foreach (var a in all)
                if (a != self && id(a) != 0)
                    yield return id(a);
        }

        // Первый ассет вида (кроме self) с тем же ненулевым Type, что у self.
        private static T FindConflict<T>(IEnumerable<T> all, T self, System.Func<T, byte> id) where T : Object
        {
            if (self == null || id(self) == 0) return null;
            byte mine = id(self);
            foreach (var a in all)
                if (a != self && id(a) == mine)
                    return a;
            return null;
        }

        // Все ассеты типа T на диске (AssetDatabase), а не из списка каталога.
        private static IEnumerable<T> LoadAll<T>() where T : Object
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    yield return asset;
            }
        }
    }
}
#endif
