using System;
using Shared.Messages;

namespace Shared.Messages.Interaction
{
    /// <summary>
    /// «Использовать» (E): взаимодействие с тайлом под игроком. Без нагрузки —
    /// сервер действует по текущему тайлу игрока (заодно анти-чит).
    /// </summary>
    public struct UseIntent : INetMessage
    {
        public MessageType Type => MessageType.UseIntent;

        public byte[] Serialize() => Array.Empty<byte>();

        public void Deserialize(byte[] data) { }
    }
}
