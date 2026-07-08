using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Вид пола (per-object SO): id == значение <see cref="Shared.World.Tile.FloorType"/> (0 = пола нет).</summary>
    [CreateAssetMenu(menuName = "Station/Floor Definition", fileName = "FloorDefinition")]
    public sealed class FloorDefinition : ScriptableObject
    {
        [Tooltip("Значение Tile.FloorType. 0 = не назначен (авто-ID заполнит в инспекторе).")]
        public byte Type = 0;
        public string DisplayName = "Floor";

        [Tooltip("Спрайт для клетки редактора. Если префаб пуст — рисуется и в игре на SpriteRenderer.")]
        public Sprite Sprite;
        [Tooltip("Префаб пола, инстансится в игре. Пусто → fallback на Sprite.")]
        public GameObject Prefab;

        [Header("Верх пола (шейдер TileReader)")]
        [Tooltip("Грид-текстура верха пола → _TopMap. Форму выбирает шейдер по _cur. Пусто → дефолт материала.")]
        public Texture2D TopMap;
        [Tooltip("Подложка верха пола → _DownTex материала (TileTop).")]
        public Texture2D BackingMap;

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
