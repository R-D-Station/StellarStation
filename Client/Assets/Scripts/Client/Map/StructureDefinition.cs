using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Вид настенного объекта (per-object SO): id == значение <see cref="Tile.StructureType"/>.</summary>
    [CreateAssetMenu(menuName = "Station/Structure Definition", fileName = "StructureDefinition")]
    public sealed class StructureDefinition : ScriptableObject
    {
        [Tooltip("Значение Tile.StructureType. 0 = объекта нет.")]
        public byte Type = 1;
        public string DisplayName = "Structure";
        [Tooltip("Стена/дверь/люк/окно. Дверь и люк — открываемые (Openable).")]
        public StructureCategory Category = StructureCategory.Wall;

        [Header("Визуал (у открываемых — закрытый/открытый)")]
        public Sprite Sprite;
        public GameObject Prefab;
        public Sprite OpenSprite;
        public GameObject OpenPrefab;

        [Header("Грани 3D-меша (шейдер TileFaceSprites)")]
        [Tooltip("Боковые грани меша. MapRenderer кладёт в _SideTex материала.")]
        public Sprite SideSprite;
        [Tooltip("Верхняя грань при ВЫКЛЮЧЕННОМ autotiling. При UseConnections верх берётся по форме (Connection.GetTopSprite).")]
        public Sprite TopSprite;

        [Header("Флаги симуляции")]
        [Tooltip("Держит обзор по горизонтали (в закрытом виде). Стекло/окно = false.")]
        public bool BlocksHorizontalSight = true;
        [Tooltip("Не пропускает газ по горизонтали (герметичность).")]
        public bool SealsHorizontal = true;

        /// <summary>Открываемый объект (дверь/люк), а не глухой (стена/окно).</summary>
        public bool Openable => Category == StructureCategory.Door || Category == StructureCategory.Hatch;

        [Header("Соединение стен (autotiling)")]
        public WallConnectionData Connection = new WallConnectionData();

        // Подсказка автору в редакторе: стена с autotiling, но заданы не все 6 мешей → формы уйдут в фолбэк.
        private void OnValidate()
        {
            if (Category != StructureCategory.Wall || !Connection.UseConnections) return;
            var c = Connection;
            if (c.MeshSingle == null || c.MeshEnd == null || c.MeshStraight == null
                || c.MeshCorner == null || c.MeshT == null || c.MeshCross == null)
                Debug.LogWarning($"[StructureDefinition '{DisplayName}'] UseConnections, но не все 6 мешей заданы", this);
        }

        /// <summary>Данные autotiling: с чем соединяется стена и 6 базовых мешей по форме. Читаются
        /// MapRenderer (W3): ApplyChunk выбирает меш по форме, ConnectsWall — соседей по флагам.</summary>
        [System.Serializable]
        public sealed class WallConnectionData
        {
            public bool UseConnections = false;          // включает autotiling (для стен → true)
            public bool ConnectsToSameType = true;
            public bool ConnectsToOtherWalls = true;
            public bool ConnectsToWindows = false;
            public bool ConnectsToDoorsHatches = true;   // A2: двери/люки считаются соседями стены
            public byte[] ConnectOnlyToTypes = System.Array.Empty<byte>(); // пусто = соединять по булям выше

            public GameObject MeshSingle, MeshEnd, MeshStraight, MeshCorner, MeshT, MeshCross;

            // Top-спрайты по форме (autotiling вкл): шейдер TileFaceSprites текстурит верх меша формы.
            // Зеркало мешам — MapRenderer берёт спрайт той же формы, что и меш.
            public Sprite TopSingle, TopEnd, TopStraight, TopCorner, TopT, TopCross;

            public GameObject GetMesh(WallShape shape) => shape switch
            {
                WallShape.Single => MeshSingle,
                WallShape.End => MeshEnd,
                WallShape.Straight => MeshStraight,
                WallShape.Corner => MeshCorner,
                WallShape.T => MeshT,
                WallShape.Cross => MeshCross,
                _ => null
            };

            public Sprite GetTopSprite(WallShape shape) => shape switch
            {
                WallShape.Single => TopSingle,
                WallShape.End => TopEnd,
                WallShape.Straight => TopStraight,
                WallShape.Corner => TopCorner,
                WallShape.T => TopT,
                WallShape.Cross => TopCross,
                _ => null
            };
        }
    }
}
