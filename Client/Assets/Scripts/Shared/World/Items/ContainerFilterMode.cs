namespace Shared.World.Items
{
    /// <summary>Режим фильтра содержимого контейнера (ItemProto.FilterMode), см. ContainerFilter.Allows.</summary>
    public enum ContainerFilterMode : byte
    {
        None = 0,
        Whitelist = 1,
        Blacklist = 2
    }
}
