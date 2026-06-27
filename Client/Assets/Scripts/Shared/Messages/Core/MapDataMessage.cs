using System.IO;
using Shared.Messages;
using Shared.World;

namespace Shared.Messages.Core
{
    /// <summary>Карта целиком от сервера клиенту (формат .smap, см. MapSerializer).</summary>
    public struct MapDataMessage : INetMessage
    {
        public GridMap Map;

        public MessageType Type => MessageType.MapData;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            MapSerializer.Write(ms, Map);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            Map = MapSerializer.Read(ms);
        }
    }
}
