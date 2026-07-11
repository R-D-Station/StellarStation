using UnityEngine;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Вид блока (per-type SO): id == BlockType в картах v10. Источник кодогена BlockCatalogData.</summary>
    [CreateAssetMenu(menuName = "Station/Block Definition", fileName = "BlockDefinition")]
    public sealed class BlockDefinition : ScriptableObject
    {
        [Tooltip("BlockType (ushort). 0 = не назначен (авто-ID заполнит в инспекторе). Write-once: id запечён в карты v10, не перенумеровывать.")]
        public ushort Type = 0;
        public string DisplayName = "Block";
        [Tooltip("Категория: Door/Hatch — открываемые; Marker — невидимый триггер-блок (виден только в редакторе).")]
        public BlockCategory Category = BlockCategory.Generic;

        [Header("Грани")]
        [Tooltip("Герметичные грани (атмос). Оси Unity: YPos = верх, YNeg = низ.")]
        public BlockFaceFlags SealsFaces = BlockFaceFlags.All;
        [Tooltip("Непрозрачные грани (обзор/окклюзия/потолок-детект).")]
        public BlockFaceFlags OpaqueFaces = BlockFaceFlags.All;

        [Header("Коллизия")]
        [Tooltip("AABB в осях Unity (Y — высота), координаты блока [0..1]. Пусто = без коллизии. Квантуется 1/16 при кодогене.")]
        public CollisionBox[] CollisionBoxes = { CollisionBox.Full };

        [Header("Деконструкция")]
        [Tooltip("Число стадий (0 = не деконструируется). Рецепты/инструменты — будущий контент.")]
        public byte DeconstructStages = 0;

        [Header("Размер")]
        [Tooltip("Габарит объекта в блоках (X — ширина, Y — высота, Z — глубина). Оси 1..2, частей ≤ 4 (ёмкость part-бит). Дверь = 2×2×1.")]
        public Vector3Int Size = Vector3Int.one;

        [Header("Соединения (autotiling)")]
        public BlockConnectionData Connection = new BlockConnectionData();

        [Header("Верх (шейдер TileReader)")]
        [Tooltip("Грид-текстура верха → _TopMap; форму выбирает шейдер по _cur (автотекстуринг). Пусто → без текстурного верха.")]
        public Texture2D TopMap;
        [Tooltip("Подложка верха → _DownTex.")]
        public Texture2D BackingMap;
        [Tooltip("Колонок в грид-атласе верха → _count_x (как в TileTop.mat).")]
        public int TopMapCountX = 5;
        [Tooltip("Рядов в грид-атласе верха → _count_y (верхний ряд — формы, нижний — углы).")]
        public int TopMapCountY = 2;
        [Tooltip("Калибровка поворотов верха (общий ассет на атлас). Пусто → без поправок.")]
        public TopGridCalibration TopCalibration;

        [Header("Грани мешей (TileFaceSprites)")]
        [Tooltip("Спрайт БОКОВЫХ граней мешей → _SideTex (шейдер кормится per-инстанс). Пусто → белый.")]
        public Texture2D FaceSideTex;
        [Tooltip("Спрайт ВЕРХНЕЙ грани мешей → _TopTex. Пусто → белый.")]
        public Texture2D FaceTopTex;

        [Header("Визуал (до дизайн-пасса рендера)")]
        [Tooltip("Префаб блока. Пусто → серые кубы по боксам. Резолв по id через Resources — ассет BlockDefinition должен лежать в папке Resources.")]
        public GameObject Prefab;
        [Tooltip("Где у префаба пивот: Низ (центр низа объекта) или Центр (модель без обвязки, как у Unity-примитивов).")]
        public BlockPivot Pivot = BlockPivot.BottomCenter;

        public enum BlockPivot : byte
        {
            BottomCenter = 0,
            Center = 1
        }

        /// <summary>Вертикальное смещение точки установки префаба под его пивот (Центр → половина высоты объекта).</summary>
        public float PivotYOffset => Pivot == BlockPivot.Center ? Size.y * 0.5f : 0f;

        // Подсказка автору: автотайл включён, но заданы не все 6 мешей → эти формы молча уйдут в фолбэк (Prefab/кубы).
        private void OnValidate()
        {
            if (Connection == null || !Connection.UseConnections) return;
            var c = Connection;
            if (c.MeshSingle == null || c.MeshEnd == null || c.MeshStraight == null
                || c.MeshCorner == null || c.MeshT == null || c.MeshCross == null)
                Debug.LogWarning($"[BlockDefinition '{DisplayName}'] UseConnections, но не все 6 мешей заданы", this);
        }
        [Tooltip("Спрайт превью в редакторе; для маркеров — единственный визуал (Editor-only).")]
        public Sprite EditorSprite;

        /// <summary>Автотайл блока: с чем соединяться (4 план-соседа на той же высоте) и 6 мешей по форме.
        /// Конвенция: базовый меш соединением на север (+Z), поворот 90°-шагами по часовой (BlockShapeResolver).
        /// Пустой меш формы → фолбэк на Prefab/кубы.</summary>
        [System.Serializable]
        public sealed class BlockConnectionData
        {
            [Tooltip("Выбирать меш по 4 соседям той же высоты.")]
            public bool UseConnections = false;
            [Tooltip("Соединяться с блоками того же типа.")]
            public bool ConnectsToSameType = true;
            [Tooltip("Соединяться с другими блоками, у которых тоже включён автотайл.")]
            public bool ConnectsToOtherConnected = true;

            public GameObject MeshSingle, MeshEnd, MeshStraight, MeshCorner, MeshT, MeshCross;

            public GameObject GetMesh(Shared.World.WallShape shape) => shape switch
            {
                Shared.World.WallShape.Single => MeshSingle,
                Shared.World.WallShape.End => MeshEnd,
                Shared.World.WallShape.Straight => MeshStraight,
                Shared.World.WallShape.Corner => MeshCorner,
                Shared.World.WallShape.T => MeshT,
                Shared.World.WallShape.Cross => MeshCross,
                _ => null
            };
        }

        /// <summary>Редактируемый AABB (center+size, оси Unity: Y — высота). Бейкается в BlockBox (1/16).</summary>
        [System.Serializable]
        public struct CollisionBox
        {
            public Vector3 Center;
            public Vector3 Size;

            public static CollisionBox Full => new CollisionBox
            {
                Center = new Vector3(0.5f, 0.5f, 0.5f),
                Size = Vector3.one
            };

            /// <summary>Слаб пола 0.25, зафиксирован по верху блока (Y ∈ [0.75..1]).</summary>
            public static CollisionBox SlabTop => new CollisionBox
            {
                Center = new Vector3(0.5f, 0.875f, 0.5f),
                Size = new Vector3(1f, 0.25f, 1f)
            };
        }
    }
}
