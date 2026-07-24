using Shared.Messages;

namespace Server.Network.Messages
{
    /// <summary>Таблица wire-id → обработчик клиентских сообщений; заменяет switch в GameServer.OnNetworkReceive.</summary>
    public sealed class MessageRouter
    {
        private readonly IClientMessageHandler?[] _table; // индекс по MessageType — без аллокаций на диспетче

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

        /// <summary>Полный список клиент→сервер хендлеров; новое сообщение — новая строка здесь + запись в MessageRouterTests.ClientToServerTypes.</summary>
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

        /// <summary>Хендлер по wire-id или null (server→client id / неизвестный id).</summary>
        public IClientMessageHandler? Resolve(ushort typeId) => typeId < _table.Length ? _table[typeId] : null;

        /// <summary>Разбирает и обрабатывает один кадр; битый пакет только логируется (не кикает), т.к. Deserialize теперь тоже под try/catch.</summary>
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
