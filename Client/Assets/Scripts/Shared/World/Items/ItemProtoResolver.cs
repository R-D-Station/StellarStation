using System;
using System.Collections.Generic;

namespace Shared.World.Items
{
    public static class ItemProtoResolver
    {
        private const byte DefaultSlotSpan = 1;
        private const byte DefaultMaxStack = 1;
        private const byte DefaultMaxContents = 0;
        private const SlotCategory DefaultEquipSlot = SlotCategory.None;

        public static IReadOnlyList<ItemProto> Resolve(IReadOnlyList<ItemProtoRaw> raw)
        {
            var byId = new Dictionary<ushort, ItemProtoRaw>(raw.Count);
            foreach (var r in raw)
            {
                if (!byId.TryAdd(r.ItemDefId, r))
                    throw new InvalidOperationException($"Duplicate ItemDefId {r.ItemDefId}");
            }

            var result = new List<ItemProto>(raw.Count);
            foreach (var r in raw)
            {
                SlotCategory equip = SlotCategory.Inherit;
                byte span = 0;
                byte stack = 0;
                byte contents = 0;

                var seen = new HashSet<ushort>();
                var cur = r;
                while (true)
                {
                    if (!seen.Add(cur.ItemDefId))
                        throw new InvalidOperationException($"Parent cycle at ItemDefId {cur.ItemDefId}");

                    if (equip == SlotCategory.Inherit && cur.EquipSlot != SlotCategory.Inherit) equip = cur.EquipSlot;
                    if (span == 0 && cur.SlotSpan != 0) span = cur.SlotSpan;
                    if (stack == 0 && cur.MaxStack != 0) stack = cur.MaxStack;
                    if (contents == 0 && cur.MaxContents != 0) contents = cur.MaxContents;

                    if (cur.ParentDefId < 0) break;
                    ushort parentId = (ushort)cur.ParentDefId;
                    if (!byId.TryGetValue(parentId, out var parent))
                        throw new InvalidOperationException($"ItemDefId {cur.ItemDefId} references missing parent {cur.ParentDefId}");
                    cur = parent;
                }

                if (equip == SlotCategory.Inherit) equip = DefaultEquipSlot;
                if (span == 0) span = DefaultSlotSpan;
                if (stack == 0) stack = DefaultMaxStack;
                if (contents == 0) contents = DefaultMaxContents;

                result.Add(new ItemProto(r.ItemDefId, equip, r.Equippable, span, stack, r.IsContainer, contents, r.HasCollision, r.CollisionBox, r.Pullable,
                    r.Tags, r.FilterMode, r.FilterItemIds, r.FilterTagIds, r.ContainerMode, r.SuckRadius));
            }
            return result;
        }
    }
}
