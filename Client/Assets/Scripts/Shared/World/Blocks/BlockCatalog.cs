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
        Marker = 7
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
        private readonly BlockBox[][][] _boxes;

        public bool IsMulti => SizeX * SizeY * SizeZ > 1;
        public int PartCount => _boxes.Length;

        public bool Openable => Category == BlockCategory.Door || Category == BlockCategory.Hatch;
        public bool IsMarker => Category == BlockCategory.Marker;

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

        /// <summary>Боксы конкретной части с учётом поворота (part/facing — из state-байта позиции).</summary>
        public BlockBox[] GetBoxes(int part, int facing)
            => _boxes[part < _boxes.Length ? part : 0][facing & 3];

        public BlockInfo(ushort id, string name, BlockCategory category,
            BlockFaceFlags sealsFaces, BlockFaceFlags opaqueFaces, byte deconstructStages, BlockBox[] boxes,
            byte sizeX = 1, byte sizeY = 1, byte sizeZ = 1)
            : this(id, name, category, sealsFaces, opaqueFaces, deconstructStages,
                   new[] { boxes ?? System.Array.Empty<BlockBox>() }, sizeX, sizeY, sizeZ)
        {
        }

        /// <summary>Основной ктор: боксы по частям (facing 0, порядок частей — MultiBlock).</summary>
        public BlockInfo(ushort id, string name, BlockCategory category,
            BlockFaceFlags sealsFaces, BlockFaceFlags opaqueFaces, byte deconstructStages, BlockBox[][] partBoxes,
            byte sizeX = 1, byte sizeY = 1, byte sizeZ = 1)
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

            if (partBoxes == null || partBoxes.Length == 0)
                partBoxes = new[] { System.Array.Empty<BlockBox>() };

            _boxes = new BlockBox[partBoxes.Length][][];
            for (int p = 0; p < partBoxes.Length; p++)
            {
                var f0 = partBoxes[p] ?? System.Array.Empty<BlockBox>();
                _boxes[p] = new BlockBox[4][];
                _boxes[p][0] = f0;
                for (int f = 1; f < 4; f++)
                {
                    var prev = _boxes[p][f - 1];
                    var rot = new BlockBox[prev.Length];
                    for (int i = 0; i < prev.Length; i++)
                        rot[i] = prev[i].RotatedCW();
                    _boxes[p][f] = rot;
                }
            }
        }
    }

    /// <summary>
    /// Каталог типов блоков: id → данные. Наполняется из сгенерированного BlockCatalogData
    /// (кодоген из BlockDefinition SO — единственный источник, руками не заполнять).
    /// </summary>
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

        public static int Count => _byId.Count;

        /// <summary>Данные типа; неизвестный id безопасно даёт Air (карта новее каталога).</summary>
        public static BlockInfo Get(ushort id) => _byId.TryGetValue(id, out var info) ? info : Air;

        public static IEnumerable<BlockInfo> All => _byId.Values;
    }
}
