using UnityEngine;
using Shared.World;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Автотекстуринг верхней грани блока: форма+углы по план-соседям в дочерний TileReader-меш префаба (порт тайлового автотайла).</summary>
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

        private static Shader _tileReader;
        private static MaterialPropertyBlock _mpb;

        /// <summary>Перекрыт ли верх блока объектом сверху (воздух и CoversBlockBelow=false — не перекрывают).</summary>
        public static bool CoveredAbove(BlockGrid grid, int x, int y, int z)
        {
            ushort above = grid.GetBlock(x, y, z);
            if (above == 0)
                return false;
            var d = BlockDefinitionResolver.Find(above);
            return d == null || d.CoversBlockBelow;
        }

        /// <summary>Дочерний меш префаба на шейдере Custom/TileReader (верх-грид). Null → префаб верх не несёт.</summary>
        public static MeshRenderer FindTopRenderer(GameObject instance)
        {
            if (_tileReader == null)
                _tileReader = Shader.Find("Custom/TileReader");
            if (_tileReader == null || instance == null)
                return null;
            var rends = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var m = rends[i].sharedMaterial;
                if (m != null && m.shader == _tileReader)
                    return rends[i];
            }
            return null;
        }

        /// <summary>Соединён ли сосед автотайлом: свой тип (ConnectsToSameType) или явный список ConnectsToTypes, и его основание на том же уровне (localY==0).</summary>
        public static bool ConnectsTo(BlockGrid grid, BlockDefinition selfDef, ushort selfType, int x, int y, int z)
        {
            ushort other = grid.GetBlock(x, y, z);
            if (other == 0)
                return false;

            if (grid.TryGetStructOffset(x, y, z, out _, out int dy, out _) && dy != 0)
                return false;

            var conn = selfDef.Connection;
            if (conn == null)
                return false;
            if (conn.ConnectsToSameType && other == selfType)
                return true;
            var list = conn.ConnectsToTypes;
            if (list != null)
                for (int i = 0; i < list.Length; i++)
                    if (list[i] == other)
                        return true;
            return false;
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

        /// <summary>Кормит дочерний TileReader-меш верхом через MPB; visible=false шлёт тот же полный грид с _alpha=0 (не голую альфу — иначе раскрытый позже верх безтекстурный).</summary>
        public static TopParams FeedTopMesh(MeshRenderer r, BlockDefinition def,
                                            WallShape shape, int steps, byte cornerMask, bool visible)
        {
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();

            var (curCorner, cornerRotate) = WallCornerCode.EncodeCorners(cornerMask);
            bool isFloor = def.Category == BlockCategory.Floor;
            int floorBase = isFloor ? WallCornerCode.FloorCornerBaseQuarter : 0;
            var cal = def.TopCalibration;
            // effSteps = steps(префаб) + mainQ — на неё же завязана компенсация углового слоя ниже.
            int mainQ = cal != null ? cal.Main(shape) : 0;
            int effSteps = (steps + mainQ) & 3;
            byte rotate = (byte)((WallCornerCode.CompensateRotateForMesh(cornerRotate, effSteps)
                + floorBase + WallCornerCode.ShapeCornerQuarter(isFloor, shape, curCorner)
                + (cal != null ? cal.Corner(shape, curCorner) : 0)) & 3);
            int cur = WallCornerCode.CurFromShape(shape);

            // MPB строим С НУЛЯ: пул префабов переиспользуется между типами → мержить старый блок нельзя.
            _mpb.SetTexture(TopMapId, def.TopMap != null ? (Texture)def.TopMap : Texture2D.blackTexture);
            _mpb.SetTexture(DownTexId, def.BackingMap != null ? (Texture)def.BackingMap : Texture2D.blackTexture);
            _mpb.SetInteger(CurId, cur);
            _mpb.SetInteger(CurCornerId, curCorner);
            _mpb.SetInteger(RotateId, rotate);
            _mpb.SetInteger(CountXId, Mathf.Max(1, def.TopMapCountX));
            _mpb.SetInteger(CountYId, Mathf.Max(1, def.TopMapCountY));
            _mpb.SetFloat(AlphaId, visible ? 1f : 0f);
            r.SetPropertyBlock(_mpb);

            ApplyMainRotation(r, mainQ);
            return new TopParams(shape, effSteps, cur, curCorner, rotate);
        }

        // Сброс к запечённой позе перед доворотом — иначе поза копится при переиспользовании инстанса из пула.
        private static void ApplyMainRotation(MeshRenderer r, int mainQ)
        {
            var t = r.transform;
            var origin = t.GetComponent<TopMeshOrigin>();
            if (origin == null)
            {
                origin = t.gameObject.AddComponent<TopMeshOrigin>();
                origin.LocalPos = t.localPosition;
                origin.LocalRot = t.localRotation;
                origin.Captured = true;
            }
            t.localPosition = origin.LocalPos;
            t.localRotation = origin.LocalRot;
            if ((mainQ & 3) == 0)
                return;
            var c = r.bounds.center;
            t.RotateAround(new Vector3(c.x, t.position.y, c.z), Vector3.up, 90f * mainQ);
        }

        /// <summary>Кормит боковой (WallRenderers) меш через MPB — SideMap в _TopMap поверх own-параметров материала стены (_count/подложка своя, физического поворота нет).</summary>
        public static void FeedSideMesh(MeshRenderer r, BlockDefinition def, WallShape shape, byte cornerMask)
        {
            _mpb ??= new MaterialPropertyBlock(); // гард на вызов без предшествующего FeedTopMesh
            _mpb.Clear(); // Get не чистит чужие свойства шаренного MPB — Clear обязателен ДО Get, иначе заражение с верха
            r.GetPropertyBlock(_mpb);
            if (def.SideMap != null)
                _mpb.SetTexture(TopMapId, def.SideMap);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Кормит все рендереры верха/боков блока через BlockMaterials-холдер (или FindTopRenderer-фолбэк без холдера).</summary>
        public static TopParams FeedGrids(GameObject instance, BlockGrid grid, BlockDefinition def, ushort type,
                                          int x, int y, int z, int rotSteps, out byte corners)
        {
            corners = 0;
            if (instance == null || def == null)
                return default;

            var holder = instance.GetComponentInChildren<BlockMaterials>(true);
            MeshRenderer fallbackTop = holder == null ? FindTopRenderer(instance) : null;
            if (holder == null && fallbackTop == null)
                return default;

            bool topVisible = def.TopMap != null && !CoveredAbove(grid, x, y + def.Size.y, z);
            Resolve(grid, def, type, x, y, z, out var shape, out _, out corners);

            if (holder == null)
                return FeedTopMesh(fallbackTop, def, shape, rotSteps, corners, topVisible);

            TopParams result = default;
            var tops = holder.TopRenderers;
            if (tops != null)
                for (int i = 0; i < tops.Length; i++)
                    if (tops[i] != null)
                        result = FeedTopMesh(tops[i], def, shape, rotSteps, corners, topVisible);
            var walls = holder.WallRenderers;
            if (walls != null)
                for (int i = 0; i < walls.Length; i++)
                    if (walls[i] != null)
                        FeedSideMesh(walls[i], def, shape, corners);
            return result;
        }
    }
}
