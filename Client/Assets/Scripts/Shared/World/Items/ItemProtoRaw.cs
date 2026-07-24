using Shared.World.Blocks;

namespace Shared.World.Items
{
    /// <summary>Сырая запись одного ItemDefinition (с непогашенным наследованием) — вход ItemProtoResolver'а.</summary>
    public readonly struct ItemProtoRaw
    {
        public readonly ushort ItemDefId;
        /// <summary>Id родителя по цепочке наследования; -1 = корень.</summary>
        public readonly int ParentDefId;
        // EquipSlot=Inherit / SlotSpan,MaxStack,MaxContents=0 — сентинелы "не задано", резолвер идёт по цепочке к первому не-сентинелу.
        public readonly SlotCategory EquipSlot;
        public readonly bool Equippable;
        public readonly byte SlotSpan;
        public readonly byte MaxStack;
        public readonly bool IsContainer;
        public readonly byte MaxContents;
        public readonly bool HasCollision;
        public readonly BlockBox CollisionBox;
        public readonly bool Pullable;
        public readonly ushort[] Tags;
        public readonly ContainerFilterMode FilterMode;
        public readonly ushort[] FilterItemIds;
        public readonly ushort[] FilterTagIds;
        public readonly ContainerMode ContainerMode;
        public readonly float SuckRadius;

        public ItemProtoRaw(ushort itemDefId, int parentDefId, SlotCategory equipSlot, bool equippable, byte slotSpan,
            byte maxStack, bool isContainer, byte maxContents, bool hasCollision = false, BlockBox collisionBox = default, bool pullable = false,
            ushort[] tags = null, ContainerFilterMode filterMode = ContainerFilterMode.None,
            ushort[] filterItemIds = null, ushort[] filterTagIds = null,
            ContainerMode containerMode = ContainerMode.UI, float suckRadius = 1.5f)
        {
            ItemDefId = itemDefId;
            ParentDefId = parentDefId;
            EquipSlot = equipSlot;
            Equippable = equippable;
            SlotSpan = slotSpan;
            MaxStack = maxStack;
            IsContainer = isContainer;
            MaxContents = maxContents;
            HasCollision = hasCollision;
            CollisionBox = collisionBox;
            Pullable = pullable;
            Tags = tags;
            FilterMode = filterMode;
            FilterItemIds = filterItemIds;
            FilterTagIds = filterTagIds;
            ContainerMode = containerMode;
            SuckRadius = suckRadius;
        }
    }
}
