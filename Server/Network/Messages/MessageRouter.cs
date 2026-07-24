using Shared.Messages;

namespace Server.Network.Messages
{
    public sealed class MessageRouter
    {
        private readonly IClientMessageHandler?[] _table;

        public MessageRouter(IReadOnlyList<IClientMessageHandler> handlers)
        {
            int max = 0;
            for (int i = 0; i < handlers.Count; i++)
                max = Math.Max(max, (ushort)handlers[i].Type);

            _table = new IClientMessageHandler?[max + 1];
            for (int i = 0; i < handlers.Count; i++)
            {
                ushort id = (ushort)handlers[i].Type;
                if (_table[id] != null)
                    throw new InvalidOperationException($"Duplicate handler for {handlers[i].Type}");
                _table[id] = handlers[i];
            }
        }

        public static MessageRouter CreateDefault() => new MessageRouter(new IClientMessageHandler[]
        {
            new MoveIntentHandler(),
            new UseIntentHandler(),
            new InteractIntentHandler(),
            new PickupItemHandler(),
            new DropItemHandler(),
            new SwapHandHandler(),
            new MoveSlotHandler(),
            new OpenContainerHandler(),
            new CloseContainerHandler(),
            new PutInContainerHandler(),
            new TakeFromContainerHandler(),
            new PullItemHandler(),
        });

        public IClientMessageHandler? Resolve(ushort typeId) => typeId < _table.Length ? _table[typeId] : null;

        public void Dispatch(ClientConnection client, ushort typeId, byte[] data)
        {
            var handler = Resolve(typeId);
            if (handler == null)
            {
                Console.WriteLine($"[Server] Unknown message type from #{client.ConnectionId}: {(MessageType)typeId}");
                return;
            }

            try
            {
                handler.Handle(client, data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Bad {handler.LogName} from #{client.ConnectionId}: {ex.Message}");
            }
        }
    }
}
