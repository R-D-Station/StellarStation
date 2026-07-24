using Shared.World.Items;

namespace ServerTests.Shared.World.Items
{
    public class ContainerFilterTests
    {
        private static ItemProto Container(ContainerFilterMode mode, ushort[] itemIds = null, ushort[] tagIds = null)
            => new ItemProto(1, SlotCategory.None, false, 1, 1, true, 4, filterMode: mode, filterItemIds: itemIds, filterTagIds: tagIds);

        private static ItemProto Item(ushort id, ushort[] tags = null)
            => new ItemProto(id, SlotCategory.None, false, 1, 1, false, 0, tags: tags);

        [Fact]
        public void None_AllowsEverything()
        {
            var c = Container(ContainerFilterMode.None);
            Assert.True(ContainerFilter.Allows(c, Item(60)));
            Assert.True(ContainerFilter.Allows(c, Item(61, new ushort[] { 5 })));
        }

        [Fact]
        public void Whitelist_ById_AllowsListed_RejectsOthers()
        {
            var c = Container(ContainerFilterMode.Whitelist, itemIds: new ushort[] { 60 });
            Assert.True(ContainerFilter.Allows(c, Item(60)));
            Assert.False(ContainerFilter.Allows(c, Item(61)));
        }

        [Fact]
        public void Whitelist_ByTag_AllowsTagged_RejectsRest()
        {
            var c = Container(ContainerFilterMode.Whitelist, tagIds: new ushort[] { 7 });
            Assert.True(ContainerFilter.Allows(c, Item(60, new ushort[] { 7 })));
            Assert.False(ContainerFilter.Allows(c, Item(61, new ushort[] { 9 })));
            Assert.False(ContainerFilter.Allows(c, Item(62)));
        }

        [Fact]
        public void Blacklist_ById_RejectsListed_AllowsOthers()
        {
            var c = Container(ContainerFilterMode.Blacklist, itemIds: new ushort[] { 60 });
            Assert.False(ContainerFilter.Allows(c, Item(60)));
            Assert.True(ContainerFilter.Allows(c, Item(61)));
        }

        [Fact]
        public void Blacklist_ByTag_RejectsTagged_AllowsOthers()
        {
            var c = Container(ContainerFilterMode.Blacklist, tagIds: new ushort[] { 7 });
            Assert.False(ContainerFilter.Allows(c, Item(60, new ushort[] { 7 })));
            Assert.True(ContainerFilter.Allows(c, Item(61, new ushort[] { 9 })));
        }

        [Fact]
        public void Whitelist_Empty_AllowsNothing()
        {
            var c = Container(ContainerFilterMode.Whitelist);
            Assert.False(ContainerFilter.Allows(c, Item(60)));
            Assert.False(ContainerFilter.Allows(c, Item(61, new ushort[] { 7 })));
        }

        [Fact]
        public void Blacklist_Empty_AllowsEverything()
        {
            var c = Container(ContainerFilterMode.Blacklist);
            Assert.True(ContainerFilter.Allows(c, Item(60)));
            Assert.True(ContainerFilter.Allows(c, Item(61, new ushort[] { 7 })));
        }

        [Fact]
        public void NullArrays_NormalizedInCtor_NoThrow()
        {
            var c = new ItemProto(1, SlotCategory.None, false, 1, 1, true, 4, filterMode: ContainerFilterMode.Whitelist);
            Assert.NotNull(c.Tags);
            Assert.NotNull(c.FilterItemIds);
            Assert.NotNull(c.FilterTagIds);
            Assert.Empty(c.FilterItemIds);
            Assert.True(ContainerFilter.Allows(new ItemProto(2, SlotCategory.None, false, 1, 1, true, 4), Item(5)));
        }
    }
}
