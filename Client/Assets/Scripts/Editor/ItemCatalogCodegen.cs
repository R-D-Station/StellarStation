#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Client.Items;
using Shared.World.Items;

namespace Client.Editor
{
    /// <summary>Собирает ItemDefinition-ассеты в ItemProtoRaw, резолвит наследование и генерирует ItemCatalogData.g.cs.</summary>
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

            var raw = new List<ItemProtoRaw>(defs.Count);
            foreach (var def in defs)
                raw.Add(new ItemProtoRaw(def.ItemDefId, def.Parent != null ? def.Parent.ItemDefId : -1,
                    def.EquipSlot, def.Equippable, def.SlotSpan, def.MaxStack, def.IsContainer, def.MaxContents,
                    def.HasCollision, def.HasCollision ? Shared.World.Blocks.BlockBox.Full : default, def.Pullable,
                    def.Tags, def.FilterMode, FilterItemIds(def), def.FilterTags, def.ContainerMode, def.SuckRadius));

            IReadOnlyList<ItemProto> resolved;
            try
            {
                resolved = ItemProtoResolver.Resolve(raw);
            }
            catch (Exception e)
            {
                // намеренно не пишем .g.cs при ошибке резолва — лучше стейл-каталог, чем битый
                Debug.LogError($"[ItemCatalogCodegen] Разрешение прототипов провалено: {e.Message}");
                return;
            }

            File.WriteAllText(OutputPath, Emit(resolved), Encoding.UTF8);
            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"[ItemCatalogCodegen] Сгенерировано предметов: {resolved.Count} → {OutputPath}");
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
            foreach (var def in defs)
            {
                if (def.ItemDefId == 0)
                {
                    Debug.LogError($"[ItemCatalogCodegen] '{def.name}': ItemDefId не назначен (0).", def);
                    ok = false;
                }
            }
            return ok;
        }

        private static string Emit(IReadOnlyList<ItemProto> protos)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace Shared.World.Items");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ItemCatalogData");
            sb.AppendLine("    {");
            sb.AppendLine("        private static readonly Dictionary<ushort, ItemProto> _protos = new Dictionary<ushort, ItemProto>");
            sb.AppendLine("        {");
            foreach (var p in protos)
                sb.AppendLine($"            {{ {p.ItemDefId}, new ItemProto({p.ItemDefId}, SlotCategory.{p.EquipSlot}, {(p.Equippable ? "true" : "false")}, {p.SlotSpan}, {p.MaxStack}, {(p.IsContainer ? "true" : "false")}, {p.MaxContents}, {(p.HasCollision ? "true" : "false")}, {(p.HasCollision ? "Shared.World.Blocks.BlockBox.Full" : "default")}, {(p.Pullable ? "true" : "false")}, {ArrLit(p.Tags)}, ContainerFilterMode.{p.FilterMode}, {ArrLit(p.FilterItemIds)}, {ArrLit(p.FilterTagIds)}, ContainerMode.{p.ContainerMode}, {F(p.SuckRadius)}) }},");
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        public static bool TryGet(ushort defId, out ItemProto proto) => _protos.TryGetValue(defId, out proto);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static ushort[] FilterItemIds(ItemDefinition def)
        {
            var items = def.FilterItems;
            if (items == null || items.Length == 0) return Array.Empty<ushort>();
            var seen = new HashSet<ushort>();
            var result = new List<ushort>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                ushort id = items[i].ItemDefId;
                if (!seen.Add(id))
                {
                    Debug.LogWarning($"[ItemCatalogCodegen] '{def.name}': дубль фильтр-предмета ItemDefId={id} — пропущен.");
                    continue;
                }
                result.Add(id);
            }
            return result.ToArray();
        }

        private static string F(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "f"; // RU-локаль даёт запятую в float-литерале → CS1729

        private static string ArrLit(ushort[] arr)
        {
            if (arr == null || arr.Length == 0) return "null";
            var sb = new StringBuilder("new ushort[]{");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(arr[i]);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
#endif
