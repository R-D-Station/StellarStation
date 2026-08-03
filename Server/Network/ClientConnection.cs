using LiteNetLib;
using System.Collections.Concurrent;
using Shared.Messages.Core;
using Shared.Messages.Interaction;
using Shared.Simulation;
using Shared.Util;
using Shared.World.Items;

namespace Server.Network
{
    /// <summary>Held-предмет в слоте инвентаря: identity-only value-тип, БЕЗ ссылок на сущности/координат
    /// (местоположение held = сам слот). NetId==0 — пустой слот (sentinel безопасен: NetIdAllocator стартует с 1).</summary>
    public struct HeldItem
    {
        public int NetId;
        public ushort ItemDefId;
        public byte StackCount;
    }

    /// <summary>Состояние подключённого клиента: пир, координаты игрока и очередь intent'ов.</summary>
    public class ClientConnection : IWorldEntity
    {
        public NetPeer Peer { get; set; }
        public int ConnectionId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }

        // Состояние игрока
        public int PlayerNetId { get; set; }
        public int NetId => PlayerNetId; // IWorldEntity: ключ общего реестра сущностей (= PlayerNetId)
        public float X { get; set; }
        public float Y { get; set; }
        public int Z { get; set; }
        public byte Facing { get; set; }
        public PlayerState State { get; set; } // дефолт Stand(0); стампится в ProcessIntents каждый тик
        public StatusTimers Timers;            // ПОЛЕ (не property): FsmLogic.Step берёт ref; декремент в ProcessStatus
        public LayingReason CurrentLayingReason; // причина текущего Laying (Voluntary/KnockedDown); дефолт None
        public bool DisableMovement;             // порт Entity.cs:13 — независимая блокировка движения (Dead/Unconscious/future)
        public bool IgnoreCollision;             // порт Entity.cs:14 — труп игнорит коллизию (forward; entity-коллизий пока нет)

        // Авторитетная скорость игрока (тайлы/тик); эффективная Speed.CurrentValue едет в EntitySnapshot.Speed →
        // предиктор берёт её как baseStep (клиент НЕ реконструирует и НЕ мутирует скорость локально).
        public AdvancedValue Speed = new AdvancedValue(MovementLogic.StepPerTick, minValue: 0.01f);

        // Блок-мир (B2): кинематика игрока в осях Unity (Y — высота, VY, Grounded); legacy-поля X/Y/Z выше
        // зеркалятся из неё каждый тик (X=X, Y=Mover.Z план, Z=floor(Mover.Y)).
        public Shared.Simulation.Blocks.BlockMoverState Mover;

        // Рантайм-модификаторы скорости (scaffolding; баффы/предметы — триггеров нет, зовётся прямо/в тестах).
        // Через СКЕЙЛЫ (∏ScaleSequentially) — минуют намеренно-оставленный double-add UpdateValue(float).
        public void AddSpeedScale(float scale) => Speed.AddScaleSum(scale);
        public void RemoveSpeedScale(float scale) => Speed.RemoveScaleSum(scale);

        // Entry-API server-only состояний (Этап 5 — scaffolding; combat-триггеров нет, зовётся прямо/в тестах).
        // ticks clamp'ится к ≥1: предекремент `--n<=0` в ProcessStatus при ticks<=0 морозил бы на 1 лишний тик.
        public void ApplyStun(int ticks)
        {
            State = PlayerState.Stun;
            Timers.StunTicksRemaining = Math.Max(1, ticks);
        }

        public void KnockDown(int ticks)
        {
            State = PlayerState.Laying;
            CurrentLayingReason = LayingReason.KnockedDown;
            Timers.KnockdownTicksRemaining = Math.Max(1, ticks);
        }

        // Dead/Unconscious — выход внешний/future (длительности нет). DisableMovement дублирует MovementAllowed,
        // но ставится явно по таблице состояний §2 (forward-scaffolding под независимые заморозки).
        public void Kill()
        {
            State = PlayerState.Dead;
            IgnoreCollision = true;
            DisableMovement = true;
        }

        public void SetUnconscious()
        {
            State = PlayerState.Unconscious;
            DisableMovement = true;
        }

        // Для reconciliation
        public uint LastProcessedSequence { get; set; }

        // Запрос «использовать» (E): обрабатывается в game-loop.
        public bool UseRequested { get; set; }

        // Очередь intent'ов; обрабатывается в GameLoop (по одному на тик).
        public readonly ConcurrentQueue<MoveIntent> IntentQueue = new();

        // Очередь адресных интеракций (клик по тайлу/сущности); дренируется в ProcessInteractions (request-only).
        public readonly ConcurrentQueue<InteractIntent> InteractQueue = new();

        // Выбор этажа с панели в кабине; дренируется LiftFloorSystem ПОСЛЕ интеракций (request-only).
        public readonly ConcurrentQueue<Shared.Messages.Lifts.LiftFloorRequest> LiftFloorQueue = new();

        // Инвентарь: identity-only слоты, БЕЗ ссылок на _entities/GroundItemEntity. Held-предметы физически
        // удалены из общего реестра сущностей на pickup — живут ТОЛЬКО здесь, никогда не идут в PVS.
        // Slots[cat][idx], размер второй оси = InventorySlot.DefaultCount(cat) (Фаза 1, раскладка по SlotCategory).
        public readonly HeldItem[][] Slots;
        public byte ActiveHand;
        public bool SawGroundItems; // видел ли клиент наземные предметы ранее — гейт empty-transition ItemSnapshot

        public readonly ConcurrentQueue<PickupItem> PickupQueue = new();
        public readonly ConcurrentQueue<DropItem> DropQueue = new();
        public readonly ConcurrentQueue<SwapHandRequest> SwapQueue = new();
        public readonly ConcurrentQueue<MoveSlotRequest> MoveSlotQueue = new();

        public readonly HashSet<int> OpenContainers = new();
        public readonly ConcurrentQueue<OpenContainer> OpenContainerQueue = new();
        public readonly ConcurrentQueue<CloseContainer> CloseContainerQueue = new();
        public readonly ConcurrentQueue<PutInContainer> PutInContainerQueue = new();
        public readonly ConcurrentQueue<TakeFromContainer> TakeFromContainerQueue = new();

        public int PulledNetId; // NetId тянущегося предмета (0 = не тянет); эхо в PullSync владельцу
        public readonly ConcurrentQueue<PullItem> PullQueue = new();

        public int ContainedInNetId; // NetId SS14-ящика, в котором заперт игрок (0 = свободен); гейт ввода/спрайта, эхо в ContainSync

        public int ExposureTicks;
        public float LastSentPressureKpa = float.NaN;
        public float LastSentOxygenKpa = float.NaN;
        public int AtmosSyncCountdown;

        // Блочный стрим (фаза C): отправленные клиенту секции (адресация дельт — в.44B) + таймер выгрузки.
        public readonly HashSet<long> SentBlockSections = new();
        public readonly Dictionary<long, int> BlockSectionLastInRange = new();

        public ClientConnection(NetPeer peer, int connectionId)
        {
            Peer = peer;
            ConnectionId = connectionId;
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;

            Slots = new HeldItem[InventorySlot.CategoryCount][];
            for (int c = 0; c < InventorySlot.CategoryCount; c++)
                Slots[c] = new HeldItem[InventorySlot.DefaultCount((SlotCategory)c)];
        }
    }
}