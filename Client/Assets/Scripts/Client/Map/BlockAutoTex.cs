using UnityEngine;
using Shared.World;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>
    /// Автотекстуринг верха блока — порт тайлового TileReader-пайплайна: форма по 4 план-соседям
    /// (BlockShapeResolver) + слой углов (правило тайлов: оба кардинала соединены, диагональ — нет;
    /// кодировка WallCornerCode как есть). Квад верха крутится мешем на 90°·steps, углы компенсируются
    /// CompensateRotateForMesh — та же единая точка калибровки, что у тайлов. Общее для рендера и редактора.
    /// </summary>
    public static class BlockAutoTex
    {
        private static readonly int TopMapId = Shader.PropertyToID("_TopMap");
        private static readonly int DownTexId = Shader.PropertyToID("_DownTex");
        private static readonly int AlphaId = Shader.PropertyToID("_alpha");
        private static readonly int CurId = Shader.PropertyToID("_cur");
        private static readonly int CurCornerId = Shader.PropertyToID("_cur_corner");
        private static readonly int RotateId = Shader.PropertyToID("_rotate");
        private static readonly int CountXId = Shader.PropertyToID("_count_x");
        private static readonly int CountYId = Shader.PropertyToID("_count_y");

        private static Material _material;
        private static MaterialPropertyBlock _mpb;

        /// <summary>Общий материал верха (Custom/TileReader — шейдер тайлового верха, переиспользуем).</summary>
        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Custom/TileReader");
                    _material = shader != null ? new Material(shader) : null;
                    if (_material != null)
                    {
                        // Дефолты шейдера _count_x/_count_y = 0 → деление на ноль в UV; страховка на не-MPB потребителей.
                        _material.SetFloat(CountXId, 5f);
                        _material.SetFloat(CountYId, 2f);
                    }
                }
                return _material;
            }
        }

        /// <summary>Сосед соединён (та же высота, план): тот же тип либо оба с автотайлом (правила Connection).</summary>
        public static bool ConnectsTo(BlockGrid grid, BlockDefinition selfDef, ushort selfType, int x, int y, int z)
        {
            ushort other = grid.GetBlock(x, y, z);
            if (other == 0)
                return false;
            if (selfDef.Connection != null && selfDef.Connection.ConnectsToSameType && other == selfType)
                return true;
            if (selfDef.Connection == null || !selfDef.Connection.ConnectsToOtherConnected)
                return false;
            var otherDef = BlockDefinitionResolver.Find(other);
            return otherDef != null && otherDef.Connection != null && otherDef.Connection.UseConnections;
        }

        /// <summary>Форма/повороты/маска углов блока по соседям (мир N = +Z).</summary>
        public static void Resolve(BlockGrid grid, BlockDefinition def, ushort type, int x, int y, int z,
                                   out WallShape shape, out int steps, out byte cornerMask)
        {
            bool n = ConnectsTo(grid, def, type, x, y, z + 1);
            bool e = ConnectsTo(grid, def, type, x + 1, y, z);
            bool s = ConnectsTo(grid, def, type, x, y, z - 1);
            bool w = ConnectsTo(grid, def, type, x - 1, y, z);

            int mask = (n ? BlockShapeResolver.North : 0) | (e ? BlockShapeResolver.East : 0)
                     | (s ? BlockShapeResolver.South : 0) | (w ? BlockShapeResolver.West : 0);
            (shape, steps) = BlockShapeResolver.Resolve(mask);

            // Углы (внутренние вырезы): оба смежных кардинала соединены, диагональ между ними — нет.
            bool nw = ConnectsTo(grid, def, type, x - 1, y, z + 1);
            bool ne = ConnectsTo(grid, def, type, x + 1, y, z + 1);
            bool se = ConnectsTo(grid, def, type, x + 1, y, z - 1);
            bool sw = ConnectsTo(grid, def, type, x - 1, y, z - 1);
            cornerMask = 0;
            if (n && w && !nw) cornerMask |= 1; // NW
            if (n && e && !ne) cornerMask |= 2; // NE
            if (s && e && !se) cornerMask |= 4; // SE
            if (s && w && !sw) cornerMask |= 8; // SW
        }

        /// <summary>Меш автотайла по соседям + поворот (90°-шаги CW); null → автотайл выключен или меш формы не задан.</summary>
        public static GameObject ResolveMesh(BlockGrid grid, BlockDefinition def, ushort type, int x, int y, int z,
                                             out int rotSteps)
        {
            rotSteps = 0;
            if (def == null || def.Connection == null || !def.Connection.UseConnections)
                return null;
            Resolve(grid, def, type, x, y, z, out var shape, out int steps, out _);
            var mesh = def.Connection.GetMesh(shape);
            if (mesh != null)
                rotSteps = steps; // фолбэк на Prefab НЕ наследует поворот формы
            return mesh;
        }

        /// <summary>Что подано в шейдер верха (диагностика авторинга).</summary>
        public readonly struct TopParams
        {
            public readonly WallShape Shape;
            public readonly int Steps, Cur, CurCorner, Rotate;
            public TopParams(WallShape shape, int steps, int cur, int curCorner, int rotate)
            { Shape = shape; Steps = steps; Cur = cur; CurCorner = curCorner; Rotate = rotate; }
        }

        /// <summary>Настроить квад верха: позиция/поворот (90°·steps вокруг Y, лицом вверх) + MPB TileReader.
        /// topY — мировая высота верхней грани (верх коллизии блока).</summary>
        public static TopParams ConfigureQuad(GameObject quad, BlockDefinition def, int x, int z, float topY,
                                              WallShape shape, int steps, byte cornerMask)
        {
            var (curCorner, cornerRotate) = WallCornerCode.EncodeCorners(cornerMask);
            // Калибровка атласа: доп. четверть основного слоя крутит квад целиком (угловой слой компенсируется
            // через effSteps), доп. четверть угла добавляется к финальному _rotate.
            var cal = def.TopCalibration;
            int effSteps = (steps + (cal != null ? cal.Main(shape) : 0)) & 3;

            quad.transform.SetPositionAndRotation(
                new Vector3(x + 0.5f, topY + 0.002f, z + 0.5f),
                Quaternion.Euler(0f, 90f * effSteps, 0f) * Quaternion.Euler(90f, 0f, 0f));

            bool isFloor = def.Category == BlockCategory.Floor;
            int floorBase = isFloor ? WallCornerCode.FloorCornerBaseQuarter : 0;
            byte rotate = (byte)((WallCornerCode.CompensateRotateForMesh(cornerRotate, effSteps)
                + floorBase + WallCornerCode.ShapeCornerQuarter(isFloor, shape, curCorner)
                + (cal != null ? cal.Corner(shape, curCorner) : 0)) & 3);
            int cur = WallCornerCode.CurFromShape(shape);

            var r = quad.GetComponent<MeshRenderer>();
            if (r == null)
                return new TopParams(shape, effSteps, cur, curCorner, rotate);
            if (Material != null)
                r.sharedMaterial = Material;

            // Блок строим С НУЛЯ (без GetPropertyBlock): пул квадов общий на все типы — слитый старый
            // блок утёк бы (_DownTex прошлой аренды). Текстуры задаём ВСЕГДА (null → black, как дефолт шейдера).
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetTexture(TopMapId, def.TopMap != null ? (Texture)def.TopMap : Texture2D.blackTexture);
            _mpb.SetTexture(DownTexId, def.BackingMap != null ? (Texture)def.BackingMap : Texture2D.blackTexture);
            _mpb.SetInteger(CurId, cur);
            _mpb.SetInteger(CurCornerId, curCorner);
            _mpb.SetInteger(RotateId, rotate);
            _mpb.SetInteger(CountXId, Mathf.Max(1, def.TopMapCountX));
            _mpb.SetInteger(CountYId, Mathf.Max(1, def.TopMapCountY));
            _mpb.SetFloat(AlphaId, 1f);
            r.SetPropertyBlock(_mpb);
            return new TopParams(shape, effSteps, cur, curCorner, rotate);
        }

        /// <summary>Высота верхней грани блока (максимальный top его коллизии; без боксов — полный блок).</summary>
        public static float TopHeight(Shared.Simulation.Blocks.IBlockShapes shapes, ushort type, byte state)
        {
            var boxes = shapes.GetBoxes(type, state);
            float top = 1f;
            if (boxes.Length > 0)
            {
                top = 0f;
                for (int i = 0; i < boxes.Length; i++)
                    if (boxes[i].MaxYf > top)
                        top = boxes[i].MaxYf;
            }
            return top;
        }
    }
}
