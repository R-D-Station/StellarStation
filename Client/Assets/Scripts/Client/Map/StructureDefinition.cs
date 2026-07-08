using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Вид настенного объекта (per-object SO): id == значение <see cref="Tile.StructureType"/>.</summary>
    [CreateAssetMenu(menuName = "Station/Structure Definition", fileName = "StructureDefinition")]
    public sealed class StructureDefinition : ScriptableObject
    {
        [Tooltip("Значение Tile.StructureType. 0 = не назначен (авто-ID заполнит в инспекторе).")]
        public byte Type = 0;
        public string DisplayName = "Structure";
        [Tooltip("Стена/дверь/люк/окно. Дверь и люк — открываемые (Openable).")]
        public StructureCategory Category = StructureCategory.Wall;

        [Header("Визуал (у открываемых — закрытый/открытый)")]
        public Sprite Sprite;
        public GameObject Prefab;
        public Sprite OpenSprite;
        public GameObject OpenPrefab;

        [Header("Текстуры стены (шейдер TileReader)")]
        [Tooltip("Грид-текстура верха → _TopMap материала WallTop. Форму выбирает шейдер по _cur. Пусто → дефолт материала.")]
        public Texture2D TopMap;
        [Tooltip("Подложка → _DownTex материала WallTop.")]
        public Texture2D BackingMap;
        [Tooltip("Текстура боковины → _TopMap материала TileWall.")]
        public Texture2D SideMap;

        [Header("Флаги симуляции")]
        [Tooltip("Держит обзор по горизонтали (в закрытом виде). Стекло/окно = false.")]
        public bool BlocksHorizontalSight = true;
        [Tooltip("Не пропускает газ по горизонтали (герметичность).")]
        public bool SealsHorizontal = true;

        [Tooltip("Держит обзор по вертикали (Z). Лестница-проём вниз = false → видно сквозь этаж (reveal-дыра).")]
        [SerializeField] private bool _blocksVerticalSight = true;

        /// <summary>Держит ли обзор по вертикали (Z). Дефолт true (стена/окно); see-through лестница-проём = false.</summary>
        public bool BlocksVerticalSight => _blocksVerticalSight;

        [Tooltip("Только рендер: пол под этой структурой НЕ рисуется и считается отсутствующим для автотайлинга соседних полов. Пол физически остаётся (walkable).")]
        [SerializeField] private bool _noVisibleFloor;

        /// <summary>Render-only: пол под структурой не рисуется + сосед-автотайлинг считает его отсутствующим. Данные пола (Walkable/Support) НЕ меняет.</summary>
        public bool NoVisibleFloor => _noVisibleFloor;

        /// <summary>Открываемый объект (дверь/люк), а не глухой (стена/окно). Единый источник — StructureCategoryInfo.</summary>
        public bool Openable => StructureCategoryInfo.IsOpenable(Category);

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
        }
    }
}
