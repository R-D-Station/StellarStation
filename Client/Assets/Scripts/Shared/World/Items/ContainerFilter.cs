namespace Shared.World.Items
{
    /// <summary>Чистая проверка допустимости предмета в контейнере по id/тегам (белый/чёрный список из ItemProto).</summary>
    public static class ContainerFilter
    {
        /// <summary>Whitelist — допускает только совпавшие по id или тегу; Blacklist — инвертирует; None — пропускает всё.</summary>
        public static bool Allows(in ItemProto container, in ItemProto item)
        {
            if (container.FilterMode == ContainerFilterMode.None) return true;
            bool match = Contains(container.FilterItemIds, item.ItemDefId)
                         || Intersects(container.FilterTagIds, item.Tags);
            return container.FilterMode == ContainerFilterMode.Whitelist ? match : !match;
        }

        private static bool Contains(ushort[] arr, ushort value)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == value) return true;
            return false;
        }

        private static bool Intersects(ushort[] a, ushort[] b)
        {
            if (a == null || b == null) return false;
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                    if (a[i] == b[j]) return true;
            return false;
        }
    }
}
