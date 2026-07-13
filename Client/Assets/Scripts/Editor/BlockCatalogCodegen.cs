#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Client.Map;
using Shared.World.Blocks;

namespace Client.Editor
{
    /// <summary>Кодоген Shared-зеркала каталога блоков: BlockDefinition SO → BlockCatalogData.g.cs с валидацией.</summary>
    public static class BlockCatalogCodegen
    {
        private const string OutputPath = "Assets/Scripts/Shared/World/Blocks/BlockCatalogData.g.cs";

        [MenuItem("Station/Blocks/Generate Block Catalog")]
        public static void Generate()
        {
            var defs = LoadAllDefinitions();
            if (!Validate(defs))
            {
                Debug.LogError("[BlockCatalogCodegen] Генерация отменена — исправьте ошибки выше.");
                return;
            }

            defs.Sort((a, b) => a.Type.CompareTo(b.Type));
            File.WriteAllText(OutputPath, Emit(defs), Encoding.UTF8);
            AssetDatabase.ImportAsset(OutputPath);
            BlockDefinitionResolver.Invalidate(); // визуальный кэш id→SO мог устареть вместе с каталогом
            Debug.Log($"[BlockCatalogCodegen] Сгенерировано типов: {defs.Count} → {OutputPath}");
        }

        public static List<BlockDefinition> LoadAllDefinitions()
        {
            var result = new List<BlockDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:BlockDefinition"))
            {
                var def = AssetDatabase.LoadAssetAtPath<BlockDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null)
                    result.Add(def);
            }
            return result;
        }

        private static bool Validate(List<BlockDefinition> defs)
        {
            bool ok = true;
            var seen = new Dictionary<ushort, BlockDefinition>();

            foreach (var def in defs)
            {
                if (def.Type == 0)
                {
                    Debug.LogError($"[BlockCatalogCodegen] '{def.name}': id не назначен (0).", def);
                    ok = false;
                    continue;
                }
                if (seen.TryGetValue(def.Type, out var other))
                {
                    Debug.LogError($"[BlockCatalogCodegen] Дубль id {def.Type}: '{def.name}' и '{other.name}'.", def);
                    ok = false;
                }
                else
                {
                    seen[def.Type] = def;
                }

                var s = def.Size;
                if (s.x < 1 || s.y < 1 || s.z < 1 || s.x > 2 || s.y > 2 || s.z > 2
                    || s.x * s.y * s.z > Shared.World.Blocks.MultiBlock.MaxParts)
                {
                    Debug.LogError($"[BlockCatalogCodegen] '{def.name}': размер {s} недопустим (оси 1..2, частей ≤ 4).", def);
                    ok = false;
                }

                // Коллизия (закрытая + открытая) авторится в OBJECT-space [0..Size] и нарезается по частям.
                CheckClampedBoxes(def, def.CollisionBoxes, s, "коллизии", ref ok);
                CheckClampedBoxes(def, def.CollisionBoxesOpen, s, "открытой коллизии", ref ok);
                // Триггеры МОГУТ выходить за габарит (ловят подход) — кламп не проверяем, только вырожденность.
                if (def.TriggerBoxes != null)
                    foreach (var box in def.TriggerBoxes)
                        if (Mathf.RoundToInt(box.Size.x * 16f) <= 0 || Mathf.RoundToInt(box.Size.y * 16f) <= 0
                            || Mathf.RoundToInt(box.Size.z * 16f) <= 0)
                        {
                            Debug.LogError($"[BlockCatalogCodegen] '{def.name}': триггер-бокс вырождается при квантовании 1/16.", def);
                            ok = false;
                        }
            }
            return ok;
        }

        private static void CheckClampedBoxes(BlockDefinition def, BlockDefinition.CollisionBox[] boxes,
                                              Vector3Int s, string label, ref bool ok)
        {
            if (boxes == null)
                return;
            foreach (var box in boxes)
            {
                Vector3 min = box.Center - box.Size * 0.5f;
                Vector3 max = box.Center + box.Size * 0.5f;
                if (min.x < -0.001f || min.y < -0.001f || min.z < -0.001f ||
                    max.x > s.x + 0.001f || max.y > s.y + 0.001f || max.z > s.z + 0.001f)
                {
                    Debug.LogError($"[BlockCatalogCodegen] '{def.name}': бокс {label} за габаритом [0..{s.x}]×[0..{s.y}]×[0..{s.z}].", def);
                    ok = false;
                }
                if (Quant(max.x - min.x) <= 0 || Quant(max.y - min.y) <= 0 || Quant(max.z - min.z) <= 0)
                {
                    Debug.LogError($"[BlockCatalogCodegen] '{def.name}': бокс {label} вырождается при квантовании 1/16.", def);
                    ok = false;
                }
            }
        }

        private static int Quant(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 16f), 0, 16);

        private static string Emit(List<BlockDefinition> defs)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Генерируется BlockCatalogCodegen (Unity: Station/Blocks/Generate Block Catalog) из BlockDefinition SO.");
            sb.AppendLine("// НЕ править руками — файл перезаписывается целиком.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace Shared.World.Blocks");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class BlockCatalogData");
            sb.AppendLine("    {");
            sb.AppendLine("        internal static BlockInfo[] Build() => new BlockInfo[]");
            sb.AppendLine("        {");
            foreach (var def in defs)
                sb.AppendLine($"            {EmitEntry(def)},");
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string EmitEntry(BlockDefinition def)
        {
            string name = (def.DisplayName ?? def.name).Replace("\\", "\\\\").Replace("\"", "\\\"");
            int sx = def.Size.x, sy = def.Size.y, sz = def.Size.z;
            string closed = SlicePerPart(def.CollisionBoxes, sx, sy, sz);
            string open = SlicePerPart(def.CollisionBoxesOpen, sx, sy, sz);
            string triggers = EmitTriggers(def);

            return $"new BlockInfo({def.Type}, \"{name}\", BlockCategory.{def.Category}, " +
                   $"(BlockFaceFlags){(byte)def.SealsFaces}, (BlockFaceFlags){(byte)def.OpaqueFaces}, " +
                   $"{def.DeconstructStages}, {closed}, {sx}, {sy}, {sz}, " +
                   $"{open}, DoorOpening.{def.Opening}, {triggers}, {F(def.DoorCloseDelay)})";
        }

        // Нарезка object-space боксов [0..Size] на ПЕР-ЧАСТЬ [0..1] (пересечение с кубом части).
        private static string SlicePerPart(BlockDefinition.CollisionBox[] boxes, int sx, int sy, int sz)
        {
            int partCount = Shared.World.Blocks.MultiBlock.PartCount(sx, sy, sz);
            var perPart = new List<string>(partCount);
            for (int p = 0; p < partCount; p++)
            {
                Shared.World.Blocks.MultiBlock.PartToLocal(p, sx, sz, out int w, out int y, out int d);
                var slices = new List<string>();
                if (boxes != null)
                    foreach (var box in boxes)
                    {
                        Vector3 min = box.Center - box.Size * 0.5f;
                        Vector3 max = box.Center + box.Size * 0.5f;
                        float ix0 = Mathf.Max(min.x, w), ix1 = Mathf.Min(max.x, w + 1);
                        float iy0 = Mathf.Max(min.y, y), iy1 = Mathf.Min(max.y, y + 1);
                        float iz0 = Mathf.Max(min.z, d), iz1 = Mathf.Min(max.z, d + 1);
                        int qx0 = Quant(ix0 - w), qx1 = Quant(ix1 - w);
                        int qy0 = Quant(iy0 - y), qy1 = Quant(iy1 - y);
                        int qz0 = Quant(iz0 - d), qz1 = Quant(iz1 - d);
                        if (qx1 > qx0 && qy1 > qy0 && qz1 > qz0)
                            slices.Add($"new BlockBox({qx0}, {qy0}, {qz0}, {qx1}, {qy1}, {qz1})");
                    }
                perPart.Add(slices.Count == 0
                    ? "System.Array.Empty<BlockBox>()"
                    : $"new[] {{ {string.Join(", ", slices)} }}");
            }
            return $"new BlockBox[][] {{ {string.Join(", ", perPart)} }}";
        }

        // Триггеры двери: object-space AABB в 1/16, БЕЗ клампа (могут торчать за габарит). null → нет триггеров.
        private static string EmitTriggers(BlockDefinition def)
        {
            if (def.TriggerBoxes == null || def.TriggerBoxes.Length == 0)
                return "null";
            var list = new List<string>();
            foreach (var box in def.TriggerBoxes)
            {
                Vector3 min = box.Center - box.Size * 0.5f;
                Vector3 max = box.Center + box.Size * 0.5f;
                int x0 = R16(min.x), y0 = R16(min.y), z0 = R16(min.z);
                int x1 = R16(max.x), y1 = R16(max.y), z1 = R16(max.z);
                if (x1 > x0 && y1 > y0 && z1 > z0)
                    list.Add($"new TriggerBox({x0}, {y0}, {z0}, {x1}, {y1}, {z1})");
            }
            return list.Count == 0 ? "null" : $"new TriggerBox[] {{ {string.Join(", ", list)} }}";
        }

        private static int R16(float v) => Mathf.RoundToInt(v * 16f); // без клампа — триггер шире габарита
        private static string F(float v) => v.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) + "f";
    }
}
#endif
