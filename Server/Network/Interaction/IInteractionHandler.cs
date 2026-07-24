namespace Server.Network.Interaction
{
    /// <summary>Контекст одной адресной интеракции: инициатор, разрешённая ЦЕЛЕВАЯ клетка и параметры вызова.</summary>
    public readonly struct InteractContext
    {
        public readonly ClientConnection Client;
        public readonly int TileX;
        public readonly int TileY;
        public readonly int TileZ;
        public readonly byte Verb;
        public readonly byte HandIndex;

        public InteractContext(ClientConnection client, int tileX, int tileY, int tileZ, byte verb, byte handIndex)
        {
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
