using System.Collections.Generic;

namespace Shared.World.Items
{
    public static class ItemCatalogData
    {
        private static readonly Dictionary<ushort, ItemProto> _protos = new Dictionary<ushort, ItemProto>
        {
            { 3, new ItemProto(3, SlotCategory.None, false, 2, 1, true, 12, true, Shared.World.Blocks.BlockBox.Full, true, new ushort[]{1}, ContainerFilterMode.None, null, null, ContainerMode.UI, 1.5f) },
            { 4, new ItemProto(4, SlotCategory.None, false, 2, 1, true, 25, false, default, true, new ushort[]{1}, ContainerFilterMode.None, null, null, ContainerMode.SS14, 1.5f) },
            { 1, new ItemProto(1, SlotCategory.Hand, false, 1, 1, false, 0, false, default, false, null, ContainerFilterMode.None, null, null, ContainerMode.UI, 1.5f) },
            { 2, new ItemProto(2, SlotCategory.Uniform, true, 1, 1, false, 0, false, default, false, new ushort[]{2}, ContainerFilterMode.None, null, null, ContainerMode.UI, 1.5f) },
        };

        public static bool TryGet(ushort defId, out ItemProto proto) => _protos.TryGetValue(defId, out proto);
    }
}
