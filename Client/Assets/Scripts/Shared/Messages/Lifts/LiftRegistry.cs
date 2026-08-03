using System;
using System.IO;

namespace Shared.Messages.Lifts
{
    public struct LiftCabinBox
    {
        public float CenterX;
        public float Y;
        public float CenterZ;
        public float HalfX;
        public float HalfZ;
        public float Height;

        public LiftCabinBox(float centerX, float y, float centerZ, float halfX, float halfZ, float height)
        {
            CenterX = centerX;
            Y = y;
            CenterZ = centerZ;
            HalfX = halfX;
            HalfZ = halfZ;
            Height = height;
        }
    }

    /// <summary>Обслуживаемая остановка: этаж, высота пола кабины на нём и КЛЕТКА шахтной двери.
    /// Этаж без двери (техуровень) — штатная ситуация: <see cref="HasDoor"/> = false, координаты не значат
    /// ничего. Отдельного флага в проводе нет намеренно — «нет двери» кодируется самим <see cref="NoDoor"/>,
    /// чтобы флаг и координаты не могли разъехаться.</summary>
    public struct LiftStopEntry
    {
        /// <summary>Часовой «двери нет». int, а НЕ short: карта адресуется 21 битом на ось со знаком
        /// (|координата| &lt; 2^20 ≈ 1e6), поэтому short молча ОБРЕЗАЛ бы дальнюю дверь, а его MinValue
        /// (−32768) попадает внутрь допустимого диапазона и мог бы совпасть с настоящей клеткой.</summary>
        public const int NoDoor = int.MinValue;

        public byte Floor;
        public float Y;
        public int DoorX;
        public int DoorY;
        public int DoorZ;

        public LiftStopEntry(byte floor, float y, int doorX, int doorY, int doorZ)
        {
            Floor = floor;
            Y = y;
            DoorX = doorX;
            DoorY = doorY;
            DoorZ = doorZ;
        }

        /// <summary>Остановка без двери — дисплей на ней не строится.</summary>
        public static LiftStopEntry WithoutDoor(byte floor, float y)
            => new LiftStopEntry(floor, y, NoDoor, NoDoor, NoDoor);

        public bool HasDoor => !(DoorX == NoDoor && DoorY == NoDoor && DoorZ == NoDoor);
    }

    public struct LiftRegistryEntry
    {
        public int LiftId;
        public float AnchorX;
        public float AnchorZ;
        /// <summary>Упреждение закрытия дверей (тики) — вторая недостающая величина для LiftPhase.At на клиенте.</summary>
        public uint DoorLeadTicks;
        /// <summary>Тип блока кабины — клиент резолвит по нему префаб. 0 = префаба нет (синтетический стенд).</summary>
        public ushort CabinDefId;
        /// <summary>Размер планового прямоугольника шахты. Пивот кабины выводится ИЗ НЕГО ЖЕ, что и якорь
        /// (<c>AnchorX = PlanX0</c>), поэтому разъехаться они не могут — float-пивот в проводе не нужен.</summary>
        public byte PlanW, PlanD;
        /// <summary>Поворот шахты (0..3). Из плана НЕ выводится: 6×5 при facing 1 и 3 даёт один и тот же
        /// прямоугольник, а префаб при этом развёрнут на 180°.</summary>
        public byte Facing;
        /// <summary>Тип блока рельса — клиент резолвит по нему префаб модуля шахты. 0 = не рисовать.</summary>
        public ushort RailDefId;
        /// <summary>Геометрия этажей шахты: <c>Y(k) = BaseY + k*FloorStep</c>, k в [0, FloorCount).
        /// Из <see cref="Stops"/> НЕ выводится — там только ОБСЛУЖИВАЕМЫЕ этажи, а рельс стоит на каждом.</summary>
        public int BaseY;
        public byte FloorStep, FloorCount;
        public LiftCabinBox[] Boxes;
        public LiftStopEntry[] Stops;

        /// <summary>Высота этажа k — та же формула, что у сервера (<c>LiftShaftSpec.FloorYAt</c>).</summary>
        public float FloorYAt(int floor) => BaseY + floor * FloorStep;
    }

    public struct LiftRegistry : INetMessage
    {
        public const int MaxLifts = 512;
        public const int MaxBoxesPerLift = 256;
        public const int MaxStopsPerLift = 32;

        public LiftRegistryEntry[] Lifts;

        public MessageType Type => MessageType.LiftRegistry;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var lifts = Lifts ?? Array.Empty<LiftRegistryEntry>();
            writer.Write((ushort)lifts.Length);
            for (int i = 0; i < lifts.Length; i++)
            {
                ref var e = ref lifts[i];
                writer.Write(e.LiftId);
                writer.Write(e.AnchorX);
                writer.Write(e.AnchorZ);
                writer.Write(e.DoorLeadTicks);
                writer.Write(e.CabinDefId);
                writer.Write(e.PlanW);
                writer.Write(e.PlanD);
                writer.Write(e.Facing);
                writer.Write(e.RailDefId);
                writer.Write(e.BaseY);
                writer.Write(e.FloorStep);
                writer.Write(e.FloorCount);

                var boxes = e.Boxes ?? Array.Empty<LiftCabinBox>();
                writer.Write((ushort)boxes.Length);
                for (int b = 0; b < boxes.Length; b++)
                {
                    ref var box = ref boxes[b];
                    writer.Write(box.CenterX);
                    writer.Write(box.Y);
                    writer.Write(box.CenterZ);
                    writer.Write(box.HalfX);
                    writer.Write(box.HalfZ);
                    writer.Write(box.Height);
                }

                var stops = e.Stops ?? Array.Empty<LiftStopEntry>();
                writer.Write((ushort)stops.Length);
                for (int t = 0; t < stops.Length; t++)
                {
                    writer.Write(stops[t].Floor);
                    writer.Write(stops[t].Y);
                    writer.Write(stops[t].DoorX);
                    writer.Write(stops[t].DoorY);
                    writer.Write(stops[t].DoorZ);
                }
            }
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "LiftRegistry data cannot be null");

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            int count = reader.ReadUInt16();
            if (count > MaxLifts)
                throw new InvalidOperationException($"LiftRegistry: lift count {count} exceeds {MaxLifts}");

            var lifts = count == 0 ? Array.Empty<LiftRegistryEntry>() : new LiftRegistryEntry[count];
            for (int i = 0; i < count; i++)
            {
                lifts[i].LiftId = reader.ReadInt32();
                lifts[i].AnchorX = ReadFinite(reader, "AnchorX");
                lifts[i].AnchorZ = ReadFinite(reader, "AnchorZ");
                lifts[i].DoorLeadTicks = reader.ReadUInt32();
                lifts[i].CabinDefId = reader.ReadUInt16();
                lifts[i].PlanW = reader.ReadByte();
                lifts[i].PlanD = reader.ReadByte();
                lifts[i].Facing = (byte)(reader.ReadByte() & 3);
                lifts[i].RailDefId = reader.ReadUInt16();
                lifts[i].BaseY = reader.ReadInt32();
                lifts[i].FloorStep = reader.ReadByte();
                lifts[i].FloorCount = reader.ReadByte();

                int boxCount = reader.ReadUInt16();
                if (boxCount > MaxBoxesPerLift)
                    throw new InvalidOperationException($"LiftRegistry: box count {boxCount} exceeds {MaxBoxesPerLift}");

                var boxes = boxCount == 0 ? Array.Empty<LiftCabinBox>() : new LiftCabinBox[boxCount];
                for (int b = 0; b < boxCount; b++)
                    boxes[b] = new LiftCabinBox(
                        ReadFinite(reader, "CenterX"),
                        ReadFinite(reader, "Y"),
                        ReadFinite(reader, "CenterZ"),
                        ReadFinite(reader, "HalfX"),
                        ReadFinite(reader, "HalfZ"),
                        ReadFinite(reader, "Height"));
                lifts[i].Boxes = boxes;

                int stopCount = reader.ReadUInt16();
                if (stopCount > MaxStopsPerLift)
                    throw new InvalidOperationException($"LiftRegistry: stop count {stopCount} exceeds {MaxStopsPerLift}");

                var stops = stopCount == 0 ? Array.Empty<LiftStopEntry>() : new LiftStopEntry[stopCount];
                for (int t = 0; t < stopCount; t++)
                    stops[t] = new LiftStopEntry(reader.ReadByte(), ReadFinite(reader, "StopY"),
                        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                lifts[i].Stops = stops;
            }

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");

            Lifts = lifts;
        }

        private static float ReadFinite(BinaryReader reader, string field)
        {
            float v = reader.ReadSingle();
            if (float.IsNaN(v) || float.IsInfinity(v))
                throw new InvalidOperationException($"{field} is invalid (NaN or Infinity)");
            return v;
        }
    }
}
