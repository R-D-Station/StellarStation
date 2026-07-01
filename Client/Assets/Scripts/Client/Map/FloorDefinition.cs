using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Вид пола (per-object SO): id == значение <see cref="Shared.World.Tile.FloorType"/> (0 = пола нет).</summary>
    [CreateAssetMenu(menuName = "Station/Floor Definition", fileName = "FloorDefinition")]
    public sealed class FloorDefinition : ScriptableObject
    {
        [Tooltip("Значение Tile.FloorType. 0 зарезервирован под «нет пола».")]
        public byte Type = 1;
        public string DisplayName = "Floor";

        [Tooltip("Спрайт для клетки редактора. Если префаб пуст — рисуется и в игре на SpriteRenderer.")]
        public Sprite Sprite;
        [Tooltip("Префаб пола, инстансится в игре. Пусто → fallback на Sprite.")]
        public GameObject Prefab;

        [Header("Грани 3D-меша (шейдер TileFaceSprites)")]
        [Tooltip("Боковые грани меша пола. MapRenderer кладёт в _SideTex материала.")]
        public Sprite SideSprite;
        [Tooltip("Верхняя грань при ВЫКЛЮЧЕННОМ autotiling. Пол с autotiling текстурится материалом TileView (TopMap + _i).")]
        public Sprite TopSprite;

        [Header("Верх пола — материал TileView")]
        [Tooltip("Грид-текстура этого типа пола → _TopMap. Форму выбирает шейдер по _i = 4-(int)shape. Пусто → дефолт материала.")]
        public Texture2D TopMap;

        [Header("Флаги симуляции, которые даёт этот пол")]
        [Tooltip("Сплошной пол не просвечивает на этаж ниже (FOV). Решётка/стекло = false.")]
        public bool BlocksVerticalSight = true;
        [Tooltip("Не пропускает газ вниз. Решётка = false.")]
        public bool SealsVertical = true;

        [Header("Соединение пола (autotiling)")]
        public FloorConnectionData Connection = new FloorConnectionData();

        // Подсказка автору в редакторе: пол с autotiling, но заданы не все 6 мешей → формы уйдут в фолбэк.
        private void OnValidate()
        {
            if (!Connection.UseConnections) return;
            var c = Connection;
            if (c.MeshSingle == null || c.MeshEnd == null || c.MeshStraight == null
                || c.MeshCorner == null || c.MeshT == null || c.MeshCross == null)
                Debug.LogWarning($"[FloorDefinition '{DisplayName}'] UseConnections, но не все 6 мешей заданы", this);
        }

        /// <summary>Данные autotiling пола: с чем соединяется и 6 базовых мешей по форме. Читаются
        /// MapRenderer (F-runtime): ApplyChunk выбирает меш по форме, FloorConnectivity — соседей по флагам.
        /// Зеркало StructureDefinition.WallConnectionData, но без окон/дверей.</summary>
        [System.Serializable]
        public sealed class FloorConnectionData
        {
            public bool UseConnections = false;          // включает autotiling пола
            public bool ConnectsToSameType = true;
            public bool ConnectsToOtherFloors = true;
            public byte[] ConnectOnlyToTypes = System.Array.Empty<byte>(); // пусто = соединять по флагу выше

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
