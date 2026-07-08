using Shared.World;

namespace Server.Network.Interaction
{
    /// <summary>Контекст одной адресной интеракции: карта, инициатор, разрешённый ЦЕЛЫЙ тайл-цель и параметры вызова.</summary>
    public readonly struct InteractContext
    {
        public readonly GridMap Map;
        public readonly ClientConnection Client;
        public readonly int TileX;
        public readonly int TileY;
        public readonly int TileZ;
        public readonly byte Verb;
        public readonly byte HandIndex;

        public InteractContext(GridMap map, ClientConnection client, int tileX, int tileY, int tileZ, byte verb, byte handIndex)
        {
            Map = map;
            Client = client;
            TileX = tileX;
            TileY = tileY;
            TileZ = tileZ;
            Verb = verb;
            HandIndex = handIndex;
        }
    }

    /// <summary>Обработчик адресной интеракции. Реестр перебирается по порядку; первый, вернувший true, поглощает клик.</summary>
    public interface IInteractionHandler
    {
        /// <summary>Обработать интеракцию, если применима к цели; true — обработано (диспетч останавливается).</summary>
        bool TryHandle(in InteractContext ctx);
    }
}
