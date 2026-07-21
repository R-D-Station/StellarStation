#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Client.Items;

namespace Client.Editor
{
    /// <summary>Кодоген Shared-зеркала экипировки: ItemDefinition SO → ItemCatalogData.g.cs (defId→EquipSlot, только equippable).</summary>
    public static class ItemCatalogCodegen
    {
        private const string OutputPath = "Assets/Scripts/Shared/World/Items/ItemCatalogData.g.cs";

        [MenuItem("Station/Items/Generate Item Catalog")]
        public static void Generate()
        {
            var defs = LoadAll();
            if (!Validate(defs))
            {
                Debug.LogError("[ItemCatalogCodegen] Генерация отменена — исправьте ошибки выше.");
                return;
            }

            defs.Sort((a, b) => a.ItemDefId.CompareTo(b.ItemDefId));
            File.WriteAllText(OutputPath, Emit(defs), Encoding.UTF8);
            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"[ItemCatalogCodegen] Сгенерировано предметов: {defs.Count} → {OutputPath}");
        }

        private static List<ItemDefinition> LoadAll()
        {
            var result = new List<ItemDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null)
                    result.Add(def);
            }
            return result;
        }

        private static bool Validate(List<ItemDefinition> defs)
        {
            bool ok = true;
            var seen = new Dictionary<ushort, ItemDefinition>();
            foreach (var def in defs)
            {
                if (def.ItemDefId == 0)
                {
                    Debug.LogError($"[ItemCatalogCodegen] '{def.name}': ItemDefId не назначен (0).", def);
                    ok = false;
                    continue;
                }
                if (seen.TryGetValue(def.ItemDefId, out var other))
                {
                    Debug.LogError($"[ItemCatalogCodegen] Дубль ItemDefId {def.ItemDefId}: '{def.name}' и '{other.name}'.", def);
                    ok = false;
                }
                else
                {
                    seen[def.ItemDefId] = def;
                }
            }
            return ok;
        }

        private static string Emit(List<ItemDefinition> defs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("namespace Shared.World.Items");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ItemCatalogData");
            sb.AppendLine("    {");
            sb.AppendLine("        public static bool TryGetEquipSlot(ushort defId, out SlotCategory slot)");
            sb.AppendLine("        {");
            sb.AppendLine("            slot = default;");
            sb.AppendLine("            switch (defId)");
            sb.AppendLine("            {");
            foreach (var def in defs)
            {
                if (!def.Equippable) continue;
                sb.AppendLine($"                case {def.ItemDefId}: slot = SlotCategory.{def.EquipSlot}; return true;");
            }
            sb.AppendLine("                default: return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
#endif
