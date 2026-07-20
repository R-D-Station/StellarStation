using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using Shared.World.Items;
using System;

namespace Client.Net
{
    /// <summary>Обёртка над ITransport: события сервера и отправка intent'ов.</summary>
    public class NetClient
    {
        private readonly ITransport _transport;
        private uint _inputSequence;

        public event Action<WorldSnapshot> OnWorldSnapshot;
        public event Action<LoginResponse> OnLoginResponse;
        public event Action<MapDataMessage> OnMapData;
        public event Action<TileUpdate> OnTileUpdate;
        public event Action<PlayerJoined> OnPlayerJoined;
        public event Action<PlayerLeft> OnPlayerLeft;
        public event Action<ChunkData> OnChunkData;
        public event Action<ChunkUnload> OnChunkUnload;
        public event Action<ItemSnapshot> OnItemSnapshot;
        public event Action<InventorySync> OnInventorySync;
        public event Action<BlockChunkData> OnBlockChunkData;
        public event Action<BlockSectionGone> OnBlockSectionGone;
        public event Action<BlockUpdateBatch> OnBlockUpdateBatch;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected => _transport.IsConnected;

        public NetClient(ITransport transport)
        {
            _transport = transport;
            _transport.OnConnected += () => OnConnected?.Invoke();
            _transport.OnDisconnected += () => OnDisconnected?.Invoke();
            _transport.OnWorldSnapshot += snap => OnWorldSnapshot?.Invoke(snap);
            _transport.OnLoginResponse += login => OnLoginResponse?.Invoke(login);
            _transport.OnMapData += map => OnMapData?.Invoke(map);
            _transport.OnTileUpdate += tu => OnTileUpdate?.Invoke(tu);
            _transport.OnPlayerJoined += m => OnPlayerJoined?.Invoke(m);
            _transport.OnPlayerLeft += m => OnPlayerLeft?.Invoke(m);
            _transport.OnChunkData += m => OnChunkData?.Invoke(m);
            _transport.OnChunkUnload += m => OnChunkUnload?.Invoke(m);
            _transport.OnItemSnapshot += s => OnItemSnapshot?.Invoke(s);
            _transport.OnInventorySync += s => OnInventorySync?.Invoke(s);
            _transport.OnBlockChunkData += m => OnBlockChunkData?.Invoke(m);
            _transport.OnBlockSectionGone += m => OnBlockSectionGone?.Invoke(m);
            _transport.OnBlockUpdateBatch += m => OnBlockUpdateBatch?.Invoke(m);
        }

        public void Connect(string address, int port) => _transport.Connect(address, port);
        public void Disconnect() => _transport.Disconnect();
        public void Poll() => _transport.Poll();

        /// <summary>Отправить намерение движения (+биты «лечь/встать» и «прыжок»); возвращает Sequence (для reconciliation).</summary>
        public uint SendMove(IntentDirection direction, bool sprint, bool layToggle, bool jump = false)
        {
            var intent = new MoveIntent
            {
                Direction = direction,
                Sprint = sprint,
                LayToggle = layToggle,
                Jump = jump,
                Sequence = ++_inputSequence
            };
            _transport.Send(intent);
            return intent.Sequence;
        }

        /// <summary>«Использовать» (E): лестница/лифт под игроком. Без предсказания — z меняет сервер.</summary>
        public void SendUse() => _transport.Send(new UseIntent());

        /// <summary>Адресная интеракция (клик по тайлу/сущности): request-only, ReliableOrdered, без Sequence/предсказания.</summary>
        public void SendInteract(byte kind, byte verb, byte hand, int tileX, int tileY, int tileZ, int targetNetId)
        {
            _transport.Send(new InteractIntent
            {
                TargetKind = kind,
                Verb = verb,
                HandIndex = hand,
                TileX = tileX,
                TileY = tileY,
                TileZ = tileZ,
                TargetNetId = targetNetId
            });
        }

        /// <summary>Подобрать наземный предмет целиком (4.5): целевой хенд решает сервер (ActiveHand), не клиент.</summary>
        public void SendPickup(int targetNetId) => _transport.Send(new PickupItem { TargetNetId = targetNetId });

        /// <summary>Выбросить предмет из слота (4.5): сервер роняет под ноги (свои координаты), клиентские не принимаются.</summary>
        public void SendDrop(SlotCategory category, byte index) => _transport.Send(new DropItem { Category = category, Index = index });

        /// <summary>Сменить активную руку (4.5): ActiveHand серверно-авторитетен, подсветка идёт по эхо InventorySync.</summary>
        public void SendSwapHand(byte hand) => _transport.Send(new SwapHandRequest { Hand = hand });

        /// <summary>Переместить предмет между слотами инвентаря (4.5).</summary>
        public void SendMoveSlot(SlotCategory fromCategory, byte fromIndex, SlotCategory toCategory, byte toIndex) => _transport.Send(new MoveSlotRequest { FromCategory = fromCategory, FromIndex = fromIndex, ToCategory = toCategory, ToIndex = toIndex });
    }
}