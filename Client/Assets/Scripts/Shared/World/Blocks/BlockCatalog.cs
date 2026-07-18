using System;
using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>Грани блока (флаги). Оси как в Unity: X/Z — план, Y — высота (YPos — верх, YNeg — низ).</summary>
    [Flags]
    public enum BlockFaceFlags : byte
    {
        None = 0,
        XPos = 1 << 0,
        XNeg = 1 << 1,
        YPos = 1 << 2,
        YNeg = 1 << 3,
        ZPos = 1 << 4,
        ZNeg = 1 << 5,
        All = XPos | XNeg | YPos | YNeg | ZPos | ZNeg
    }

    /// <summary>Категория блока; открываемость и маркерность выводятся из неё.</summary>
    public enum BlockCategory : byte
    {
        Generic = 0,
        Floor = 1,
        Wall = 2,
        Window = 3,
        Door = 4,
        Hatch = 5,
        Ladder = 6,
        /// <summary>Невидимый триггер-блок (спавн и т.п.): виден только в редакторе, коллизия Empty.</summary>
        Marker = 7,
        FloorAnchor = 8,
        Divider = 9,
        MergeMarker = 10,
        SpawnPoint = 11
    }

    /// <summary>Как открывается дверь/люк (для Category Door/Hatch).</summary>
    public enum DoorOpening : byte
    {
        /// <summary>Сама — при входе игрока в триггер-объём.</summary>
        Auto = 0,
        /// <summary>Взаимодействием игрока (будущий контент).</summary>
        Interact = 1
    }

    /// <summary>AABB коллизии в block-local координатах (оси Unity: Y — высота), квантование 1/16 (значения 0..16).</summary>
    public readonly struct BlockBox
    {
        public readonly byte MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public BlockBox(byte minX, byte minY, byte minZ, byte maxX, byte maxY, byte maxZ)
        {
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }

        public float MinXf => MinX / 16f;
        public float MinYf => MinY / 16f;
        public float MinZf => MinZ / 16f;
        public float MaxXf => MaxX / 16f;
        public float MaxYf => MaxY / 16f;
        public float MaxZf => MaxZ / 16f;

        /// <summary>Полный блок 1×1×1.</summary>
        public static BlockBox Full => new BlockBox(0, 0, 0, 16, 16, 16);

        /// <summary>Слаб пола 0.25, зафиксированный по ВЕРХУ блока (Y ∈ [0.75..1]).</summary>
        public static BlockBox SlabTop => new BlockBox(0, 12, 0, 16, 16, 16);

        /// <summary>Поворот на 90° по часовой вокруг Y в 1/16-пространстве блока: (x,z) → (z, 16−x).</summary>
        public BlockBox RotatedCW()
            => new BlockBox(MinZ, MinY, (byte)(16 - MaxX), MaxZ, MaxY, (byte)(16 - MinX));
    }

    /// <summary>Триггер-объём двери в object-space (1/16 блока, facing 0); signed — может выходить за габарит.</summary>
    public readonly struct TriggerBox
    {
        public readonly short MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public TriggerBox(short minX, short minY, short minZ, short maxX, short maxY, short maxZ)
        {
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }

        public float MinXf => MinX / 16f;
        public float MinYf => MinY / 16f;
        public float MinZf => MinZ / 16f;
        public float MaxXf => MaxX / 16f;
        public float MaxYf => MaxY / 16f;
        public float MaxZf => MaxZ / 16f;
    }

    /// <summary>Данные типа блока (Shared-зеркало BlockDefinition; генерируется кодогеном из SO).</summary>
    public sealed class BlockInfo
    {
        public readonly ushort Id;
        public readonly string Name;
        public readonly BlockCategory Category;
        public readonly BlockFaceFlags SealsFaces;
        public readonly BlockFaceFlags OpaqueFaces;
        /// <summary>Число стадий деконструкции (0 = не деконструируется). Рецепты — будущий контент.</summary>
        public readonly byte DeconstructStages;

        /// <summary>Габарит мульти-блока (1..2 по осям, частей ≤ 4); 1×1×1 = обычный блок.</summary>
        public readonly byte SizeX, SizeY, SizeZ;

        // Боксы по [part][facing]: кодоген даёт нарезку facing=0, повороты предвычисляются в ктор.
        // _boxes — закрытое состояние, _boxesOpen — открытое (только для Openable; иначе не используется).
        private readonly BlockBox[][][] _boxes;
        private readonly BlockBox[][][] _boxesOpen;

        /// <summary>Как открывается (Door/Hatch): сама/взаимодействием.</summary>
        public readonly DoorOpening Opening;
        /// <summary>Задержка автозакрытия двери (сек) после выхода игрока из триггера.</summary>
        public readonly float CloseDelay;
        /// <summary>Триггер-объёмы авто-двери (object-space, facing 0); пусто — нет триггера.</summary>
        public readonly TriggerBox[] Triggers;

        /// <summary>Занимает больше одной позиции.</summary>
        public bool IsMulti => SizeX * SizeY * SizeZ > 1;
        public int PartCount => _boxes.Length;

        public bool Openable => Category == BlockCategory.Door || Category == BlockCategory.Hatch;
        public bool IsSpawn => Category == BlockCategory.SpawnPoint;
        public bool IsMarker => Category == BlockCategory.Marker
                                || Category == BlockCategory.Divider || Category == BlockCategory.MergeMarker
                                || Category == BlockCategory.SpawnPoint;
        public bool IsFloorAnchor => Category == BlockCategory.FloorAnchor;

        public bool HasCollision
        {
            get
            {
                for (int p = 0; p < _boxes.Length; p++)
                    if (_boxes[p][0].Length > 0)
                        return true;
                return false;
            }
        }

        /// <summary>Боксы якорной части при facing 0 (совместимость/простые случаи).</summary>
        public BlockBox[] Boxes => _boxes[0][0];

        /// <summary>Боксы части при facing (закрытое состояние). part/facing — из state-байта позиции.</summary>
        public BlockBox[] GetBoxes(int part, int facing) => GetBoxes(part, facing, false);

        /// <summary>Боксы части с учётом поворота и состояния Open (открытая дверь → набор _boxesOpen).</summary>
        public BlockBox[] GetBoxes(int part, int facing, bool open)
            => (open && Openable ? _boxesOpen : _boxes)[part < _boxes.Length ? part : 0][facing & 3];

        public BlockInfo(ushort id, string name, BlockCategory category,
            BlockFaceFlags sealsFaces, BlockFaceFlags opaqueFaces, byte deconstructStages, BlockBox[] boxes,
            byte sizeX = 1, byte sizeY = 1, byte sizeZ = 1)
            : this(id, name, category, sealsFaces, opaqueFaces, deconstructStages,
                   new[] { boxes ?? System.Array.Empty<BlockBox>() }, sizeX, sizeY, sizeZ)
        {
        }

        /// <summary>Основной ктор: боксы по частям (facing 0) плюс опционально открытое состояние двери.</summary>
        public BlockInfo(ushort id, string name, BlockCategory category,
            BlockFaceFlags sealsFaces, BlockFaceFlags opaqueFaces, byte deconstructStages, BlockBox[][] partBoxes,
            byte sizeX = 1, byte sizeY = 1, byte sizeZ = 1,
            BlockBox[][] partBoxesOpen = null, DoorOpening opening = DoorOpening.Auto,
            TriggerBox[] triggers = null, float closeDelay = 0f)
        {
            Id = id;
            Name = name;
            Category = category;
            SealsFaces = sealsFaces;
            OpaqueFaces = opaqueFaces;
            DeconstructStages = deconstructStages;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            Opening = opening;
            CloseDelay = closeDelay;
            Triggers = triggers ?? System.Array.Empty<TriggerBox>();

            if (partBoxes == null || partBoxes.Length == 0)
                partBoxes = new[] { System.Array.Empty<BlockBox>() };

            _boxes = BuildRotations(partBoxes, partBoxes.Length);
            _boxesOpen = BuildRotations(partBoxesOpen, partBoxes.Length); // пусто по части, если не задано
        }

        // Предвычислить повороты боксов частей: [part][facing] из facing-0 набора (RotatedCW по шагам).
        private static BlockBox[][][] BuildRotations(BlockBox[][] partBoxes, int partCount)
        {
            var result = new BlockBox[partCount][][];
            for (int p = 0; p < partCount; p++)
            {
                var f0 = (partBoxes != null && p < partBoxes.Length ? partBoxes[p] : null)
                         ?? System.Array.Empty<BlockBox>();
                result[p] = new BlockBox[4][];
                result[p][0] = f0;
                for (int f = 1; f < 4; f++)
                {
                    var prev = result[p][f - 1];
                    var rot = new BlockBox[prev.Length];
                    for (int i = 0; i < prev.Length; i++)
                        rot[i] = prev[i].RotatedCW();
                    result[p][f] = rot;
                }
            }
            return result;
        }
    }

    /// <summary>Каталог типов блоков: id → данные, наполняется сгенерированным BlockCatalogData.</summary>
    public static class BlockCatalog
    {
        /// <summary>Air (id 0): без коллизии, без граней.</summary>
        public static readonly BlockInfo Air = new BlockInfo(0, "Air", BlockCategory.Generic,
            BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());

        private static readonly Dictionary<ushort, BlockInfo> _byId = BuildIndex();

        private static Dictionary<ushort, BlockInfo> BuildIndex()
        {
            var map = new Dictionary<ushort, BlockInfo> { [0] = Air };
            foreach (var info in BlockCatalogData.Build())
                map[info.Id] = info;
            return map;
        }

        /// <summary>Число зарегистрированных типов (включая Air).</summary>
        public static int Count => _byId.Count;

        /// <summary>Данные типа; неизвестный id безопасно даёт Air (карта новее каталога).</summary>
        public static BlockInfo Get(ushort id) => _byId.TryGetValue(id, out var info) ? info : Air;

        public static IEnumerable<BlockInfo> All => _byId.Values;
    }
}
