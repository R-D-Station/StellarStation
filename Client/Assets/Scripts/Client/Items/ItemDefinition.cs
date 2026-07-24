using UnityEngine;
using Shared.World.Items;

namespace Client.Items
{
    /// <summary>Определение предмета (клиентское): визуал + метаданные по ItemDefId (server-authoritative id).</summary>
    [CreateAssetMenu(menuName = "Station/Item Definition", fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Tooltip("Server-authoritative id предмета (совпадает с ItemInstance.ItemDefId на проводе).")]
        public ushort ItemDefId;

        [Tooltip("Отображаемое имя (тултип/UI).")]
        public string DisplayName;

        [Tooltip("Спрайт наземного предмета.")]
        public Sprite Sprite;

        [Tooltip("Спрайт открытого контейнера в мире; пусто → рисуем закрытый.")]
        public Sprite OpenSprite;

        [Tooltip("Кастомный префаб окна контейнера; пусто → дефолтный из ContainerWindows.")]
        public Client.UI.Windows.UiWindow WindowPrefab;

        // Размер предмета как ДОЛЯ тайла (1 = целый тайл); мировой размер = RenderScale·TileSize. Инспектор показывает %↔юниты (EditorCoder).
        [SerializeField] private float _renderScale = 1f;
        public float RenderScale => _renderScale;

        // !Equippable — предмет идёт только в руки; EquipSlot валиден, только пока Equippable=true (см. ItemCatalogCodegen).
        [SerializeField] private bool _equippable;
        [SerializeField] private SlotCategory _equipSlot = SlotCategory.Inherit;
        public bool Equippable => _equippable;
        public SlotCategory EquipSlot => _equipSlot;

        // Наследование гасится на кодогене (ItemCatalogCodegen → ItemProtoResolver), рантайм цепочку не обходит.
        [SerializeField] private ItemDefinition _parent;
        public ItemDefinition Parent => _parent;

        // 0 = унаследовать от Parent (сентинел, см. ItemProtoResolver).
        [SerializeField] private byte _slotSpan;
        public byte SlotSpan => _slotSpan;

        // 0 = унаследовать от Parent (сентинел, см. ItemProtoResolver).
        [SerializeField] private byte _maxStack;
        public byte MaxStack => _maxStack;

        [SerializeField] private bool _isContainer;
        public bool IsContainer => _isContainer;

        [SerializeField] private byte _maxContents;
        public byte MaxContents => _maxContents;

        // Наземный предмет — препятствие в блок-мире (форма = целая ячейка, см. ItemProto.CollisionBox).
        [SerializeField] private bool _hasCollision;
        public bool HasCollision => _hasCollision;

        [SerializeField] private bool _pullable;
        public bool Pullable => _pullable;

        [SerializeField] private ushort[] _tags;
        public ushort[] Tags => _tags;

        [SerializeField] private ContainerFilterMode _filterMode;
        public ContainerFilterMode FilterMode => _filterMode;

        [SerializeField] private ItemDefinition[] _filterItems;
        public ItemDefinition[] FilterItems => _filterItems;

        [SerializeField] private ushort[] _filterTags;
        public ushort[] FilterTags => _filterTags;

        // UI = наше окно; SS14 = физическая крышка (E закрыл — всосал, открыл — высыпал).
        [SerializeField] private ContainerMode _containerMode;
        public ContainerMode ContainerMode => _containerMode;

        [SerializeField] private float _suckRadius = 1.5f;
        public float SuckRadius => _suckRadius;

        // Клиент-only (RenderLayerCatalog); в ItemProto/кодоген не идёт.
        [SerializeField] private ushort _renderLayer;
        public ushort RenderLayer => _renderLayer;

        // Клиент-only worn-оверлей (NetEntityView); в ItemProto/кодоген не идёт.
        [SerializeField] private bool _wornVisible;
        [SerializeField] private Sprite[] _wornSprites; // [N,S,E,W], см. Direction
        public bool WornVisible => _wornVisible;
        public Sprite[] WornSprites => _wornSprites;
    }
}
