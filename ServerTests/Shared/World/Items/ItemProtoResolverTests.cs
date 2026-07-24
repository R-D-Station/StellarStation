using System;
using System.Collections.Generic;
using System.Linq;
using Shared.World.Blocks;
using Shared.World.Items;

namespace ServerTests.Shared.World.Items
{
    public class ItemProtoResolverTests
    {
        private static ItemProto One(IReadOnlyList<ItemProtoRaw> raw, ushort id)
            => ItemProtoResolver.Resolve(raw).First(p => p.ItemDefId == id);

        [Fact]
        public void SingleRoot_AllSentinel_HardDefaults()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, false, 0) };
            var p = One(raw, 1);
            Assert.Equal(SlotCategory.None, p.EquipSlot);
            Assert.Equal((byte)1, p.SlotSpan);
            Assert.Equal((byte)1, p.MaxStack);
            Assert.False(p.Equippable);
        }

        [Fact]
        public void SingleRoot_SetValues_Kept()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Ear, true, 3, 20, false, 0) };
            var p = One(raw, 1);
            Assert.Equal(SlotCategory.Ear, p.EquipSlot);
            Assert.Equal((byte)3, p.SlotSpan);
            Assert.Equal((byte)20, p.MaxStack);
            Assert.True(p.Equippable);
        }

        [Fact]
        public void Child_InheritsUnset_OverridesSet()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Ear, true, 2, 10, false, 0),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 5, false, 0)
            };
            var c = One(raw, 2);
            Assert.Equal(SlotCategory.Ear, c.EquipSlot);
            Assert.Equal((byte)2, c.SlotSpan);
            Assert.Equal((byte)5, c.MaxStack);
        }

        [Fact]
        public void ThreeLevelChain_NearestNonSentinelWins()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Belt, false, 4, 8, false, 0),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, false, 0),
                new ItemProtoRaw(3, 2, SlotCategory.Ear, false, 0, 0, false, 0)
            };
            var leaf = One(raw, 3);
            Assert.Equal(SlotCategory.Ear, leaf.EquipSlot);
            Assert.Equal((byte)4, leaf.SlotSpan);
            Assert.Equal((byte)8, leaf.MaxStack);
        }

        [Fact]
        public void Cycle_Throws()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, 2, SlotCategory.Inherit, false, 0, 0, false, 0),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, false, 0)
            };
            Assert.ThrowsAny<Exception>(() => ItemProtoResolver.Resolve(raw));
        }

        [Fact]
        public void DuplicateId_Throws()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Ear, true, 0, 0, false, 0),
                new ItemProtoRaw(1, -1, SlotCategory.Belt, true, 0, 0, false, 0)
            };
            Assert.ThrowsAny<Exception>(() => ItemProtoResolver.Resolve(raw));
        }

        [Fact]
        public void MissingParent_Throws()
        {
            var raw = new[] { new ItemProtoRaw(2, 99, SlotCategory.Inherit, false, 0, 0, false, 0) };
            Assert.ThrowsAny<Exception>(() => ItemProtoResolver.Resolve(raw));
        }

        [Fact]
        public void ChainAllSentinel_HardDefault()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, false, 0),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, false, 0)
            };
            var c = One(raw, 2);
            Assert.Equal(SlotCategory.None, c.EquipSlot);
            Assert.Equal((byte)1, c.SlotSpan);
            Assert.Equal((byte)1, c.MaxStack);
        }

        [Fact]
        public void MaxContents_SentinelRootDefaultsZero()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, false, 0) };
            Assert.Equal((byte)0, One(raw, 1).MaxContents);
        }

        [Fact]
        public void MaxContents_SetRootKept()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 12) };
            var p = One(raw, 1);
            Assert.Equal((byte)12, p.MaxContents);
            Assert.True(p.IsContainer);
        }

        [Fact]
        public void MaxContents_InheritsFromParent()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 7),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, true, 0)
            };
            Assert.Equal((byte)7, One(raw, 2).MaxContents);
        }

        [Fact]
        public void MaxContents_ChildOverridesParent()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 7),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, true, 3)
            };
            Assert.Equal((byte)3, One(raw, 2).MaxContents);
        }

        [Fact]
        public void IsContainer_DirectFromLeaf_NotInherited()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 5),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, false, 0)
            };
            Assert.False(One(raw, 2).IsContainer);
            Assert.True(One(raw, 1).IsContainer);
        }

        [Fact]
        public void Pullable_DefaultsFalse()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, false, 0) };
            Assert.False(One(raw, 1).Pullable);
        }

        [Fact]
        public void Pullable_DirectFromLeaf_NotInherited()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, false, 0, pullable: true),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, false, 0)
            };
            Assert.True(One(raw, 1).Pullable);
            Assert.False(One(raw, 2).Pullable);
        }

        [Fact]
        public void PullableAndHasCollision_BothCarried()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.None, false, 1, 1, false, 0, hasCollision: true, collisionBox: BlockBox.Full, pullable: true) };
            var p = One(raw, 1);
            Assert.True(p.HasCollision);
            Assert.True(p.Pullable);
            Assert.Equal((byte)16, p.CollisionBox.MaxY);
        }

        [Fact]
        public void Tags_DirectFromLeaf_NotInherited()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, false, 0, tags: new ushort[] { 5 }),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, false, 0)
            };
            Assert.Equal(new ushort[] { 5 }, One(raw, 1).Tags);
            Assert.Empty(One(raw, 2).Tags);
        }

        [Fact]
        public void Filter_DirectFromLeaf_DefaultsNoneAndEmpty()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 4) };
            var p = One(raw, 1);
            Assert.Equal(ContainerFilterMode.None, p.FilterMode);
            Assert.Empty(p.FilterItemIds);
            Assert.Empty(p.FilterTagIds);
        }

        [Fact]
        public void Filter_CarriedFromLeaf()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 4, filterMode: ContainerFilterMode.Whitelist, filterItemIds: new ushort[] { 60 }, filterTagIds: new ushort[] { 7 })
            };
            var p = One(raw, 1);
            Assert.Equal(ContainerFilterMode.Whitelist, p.FilterMode);
            Assert.Equal(new ushort[] { 60 }, p.FilterItemIds);
            Assert.Equal(new ushort[] { 7 }, p.FilterTagIds);
        }

        [Fact]
        public void ContainerMode_DefaultsUI_SuckRadiusDefault()
        {
            var raw = new[] { new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 4) };
            var p = One(raw, 1);
            Assert.Equal(ContainerMode.UI, p.ContainerMode);
            Assert.Equal(1.5f, p.SuckRadius, 3);
        }

        [Fact]
        public void ContainerMode_DirectFromLeaf_NotInherited()
        {
            var raw = new[]
            {
                new ItemProtoRaw(1, -1, SlotCategory.Inherit, false, 0, 0, true, 4, containerMode: ContainerMode.SS14, suckRadius: 3f),
                new ItemProtoRaw(2, 1, SlotCategory.Inherit, false, 0, 0, true, 4)
            };
            Assert.Equal(ContainerMode.SS14, One(raw, 1).ContainerMode);
            Assert.Equal(3f, One(raw, 1).SuckRadius, 3);
            Assert.Equal(ContainerMode.UI, One(raw, 2).ContainerMode);
        }
    }
}
