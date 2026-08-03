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
                int axis = Shared.World.Blocks.MultiBlock.MaxAxis;
                if (s.x < 1 || s.y < 1 || s.z < 1 || s.x > axis || s.y > axis || s.z > axis
                    || s.x * s.y * s.z > Shared.World.Blocks.MultiBlock.MaxPartsSanity)
                {
                    Debug.LogError($"[BlockCatalogCodegen] '{def.name}': размер {s} недопустим (оси 1..{axis}, частей ≤ {Shared.World.Blocks.MultiBlock.MaxPartsSanity}).", def);
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

                CheckLift(def, ref ok);
            }
            return ok;
        }

        private static void CheckLift(BlockDefinition def, ref bool ok)
        {
            if (!def.IsLiftPart)
                return;
            var m = def.LiftModule;
            if (m.x < 1 || m.y < 1 || m.z < 1 || m.x > 255 || m.y > 255 || m.z > 255)
            {
                Debug.LogError($"[BlockCatalogCodegen] '{def.name}': модуль лифта {m} вне диапазона (оси 1..255).", def);
                ok = false;
            }
            if (def.Category != BlockCategory.Marker)
                Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}': лифт-часть обязана быть Marker (визуал рисует лифт-код).", def);
            if (def.CollisionBoxes != null && def.CollisionBoxes.Length > 0)
                Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}': у лифт-части коллизия задаётся полем «Боксы кабины», а не обычными боксами.", def);
            if (def.LiftKind == LiftPartKind.Cabin)
            {
                if (def.LiftCabinBoxes == null || def.LiftCabinBoxes.Length == 0)
                    Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}': кабина без боксов — лифт не создастся.", def);
                if (def.LiftSpeed <= 0f)
                    Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}': скорость лифта ≤ 0 — кабина не поедет.", def);
                if (def.Pivot == BlockDefinition.BlockPivot.Center)
                    Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}': Pivot = Center. К кабине PivotYOffset " +
                                     "НЕ применяется (корень = план-центр модуля ШАХТЫ на уровне пола кабины), " +
                                     $"а Size.y*0.5 = {def.Size.y * 0.5f} при модуле {m.y} — поставь BottomCenter.", def);
            }
            else if (def.LiftKind == LiftPartKind.Rail && def.LiftFloorStep < 1)
                Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}': шаг этажа < 1 — скан не соберёт штабель.", def);
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
            string closed = SlicePerPart(def.CollisionBoxes, sx, sy, sz, def, "коллизия", true);
            string open = SlicePerPart(def.CollisionBoxesOpen, sx, sy, sz, def, "открытая коллизия", false);
            string triggers = EmitTriggers(def);

            return $"new BlockInfo({def.Type}, \"{name}\", BlockCategory.{def.Category}, " +
                   $"(BlockFaceFlags){(byte)def.SealsFaces}, (BlockFaceFlags){(byte)def.OpaqueFaces}, " +
                   $"{def.DeconstructStages}, {closed}, {sx}, {sy}, {sz}, " +
                   $"{open}, DoorOpening.{def.Opening}, {triggers}, {F(def.DoorCloseDelay)}, " +
                   $"{(def.RequiresSupport ? "true" : "false")}, {EmitAttach(def)}, " +
                   $"{(def.IsAirlock ? "true" : "false")}, {EmitLiftPart(def)})";
        }

        private static string EmitLiftPart(BlockDefinition def)
        {
            if (!def.IsLiftPart)
                return "null";
            return $"new LiftPartInfo(LiftPartKind.{def.LiftKind}, " +
                   $"{def.LiftModule.x}, {def.LiftModule.y}, {def.LiftModule.z}, {def.LiftFloorStep}, " +
                   $"{F(def.LiftSpeed)}, {F(def.LiftDwellSec)}, {F(def.LiftDoorLeadSec)}, {EmitLiftBoxes(def.LiftCabinBoxes)})";
        }

        private static string EmitLiftBoxes(BlockDefinition.CollisionBox[] boxes)
        {
            if (boxes == null || boxes.Length == 0)
                return "null";
            var list = new List<string>();
            foreach (var box in boxes)
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

        private static string EmitAttach(BlockDefinition def)
        {
            if (def.AttachTo == null || def.AttachTo.Length == 0)
                return "null";
            var list = new List<string>();
            foreach (var a in def.AttachTo)
                list.Add($"AttachSurface.{a}");
            return $"new AttachSurface[] {{ {string.Join(", ", list)} }}";
        }

        private static string SlicePerPart(BlockDefinition.CollisionBox[] boxes, int sx, int sy, int sz,
                                           BlockDefinition def, string label, bool warnCoverage)
        {
            var result = MultiBlockSlicer.Slice(ToSliceBoxes(boxes), sx, sy, sz);
            ReportSlicing(def, label, sx, sy, sz, boxes, result, warnCoverage);
            return EmitPerPart(result.PerPart);
        }

        private static MultiBlockSlicer.Box[] ToSliceBoxes(BlockDefinition.CollisionBox[] boxes)
        {
            if (boxes == null || boxes.Length == 0)
                return System.Array.Empty<MultiBlockSlicer.Box>();
            var arr = new MultiBlockSlicer.Box[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                Vector3 min = boxes[i].Center - boxes[i].Size * 0.5f;
                Vector3 max = boxes[i].Center + boxes[i].Size * 0.5f;
                arr[i] = new MultiBlockSlicer.Box(min.x, min.y, min.z, max.x, max.y, max.z);
            }
            return arr;
        }

        private static void ReportSlicing(BlockDefinition def, string label, int sx, int sy, int sz,
                                          BlockDefinition.CollisionBox[] boxes, MultiBlockSlicer.Result result, bool warnCoverage)
        {
            if (warnCoverage && result.PartCount > 1 && result.EmptyParts > 0)
            {
                if (result.AnchorOnly && IsSingleUnitBox(boxes))
                    Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}' ({label}): бокс не растянут на габарит {sx}×{sy}×{sz} — коллизия только у якорной клетки, {result.EmptyParts} из {result.PartCount} частей проходимы насквозь. Растяните бокс на габарит.", def);
                else
                    Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}' ({label}): {result.EmptyParts} из {result.PartCount} частей без коллизии (для проёмов законно).", def);
            }
            foreach (var drop in result.Dropped)
                Debug.LogWarning($"[BlockCatalogCodegen] '{def.name}' ({label}): часть {drop.Part}, бокс {drop.BoxIndex} — ломтик схлопнулся при квантовании 1/16 по осям{(drop.CollapsedX ? " X" : "")}{(drop.CollapsedY ? " Y" : "")}{(drop.CollapsedZ ? " Z" : "")}.", def);
        }

        private static bool IsSingleUnitBox(BlockDefinition.CollisionBox[] boxes)
        {
            if (boxes == null || boxes.Length != 1)
                return false;
            var b = boxes[0];
            return Mathf.Abs(b.Center.x - 0.5f) < 0.001f && Mathf.Abs(b.Center.y - 0.5f) < 0.001f && Mathf.Abs(b.Center.z - 0.5f) < 0.001f
                && Mathf.Abs(b.Size.x - 1f) < 0.001f && Mathf.Abs(b.Size.y - 1f) < 0.001f && Mathf.Abs(b.Size.z - 1f) < 0.001f;
        }

        private static string EmitPerPart(BlockBox[][] perPart)
        {
            if (perPart.Length <= 1)
            {
                var inline = new List<string>(perPart.Length);
                foreach (var part in perPart)
                    inline.Add(EmitPart(part));
                return $"new BlockBox[][] {{ {string.Join(", ", inline)} }}";
            }
            var sb = new StringBuilder();
            sb.Append("new BlockBox[][]\n            {\n");
            for (int p = 0; p < perPart.Length; p++)
            {
                sb.Append("                ").Append(EmitPart(perPart[p]));
                sb.Append(p < perPart.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("            }");
            return sb.ToString();
        }

        private static string EmitPart(BlockBox[] part)
        {
            if (part.Length == 0)
                return "System.Array.Empty<BlockBox>()";
            var boxes = new List<string>(part.Length);
            foreach (var b in part)
                boxes.Add($"new BlockBox({b.MinX}, {b.MinY}, {b.MinZ}, {b.MaxX}, {b.MaxY}, {b.MaxZ})");
            return $"new[] {{ {string.Join(", ", boxes)} }}";
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
