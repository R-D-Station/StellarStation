using System;
using System.Collections.Generic;

namespace Shared.World.Blocks
{
    public static class MultiBlockSlicer
    {
        public readonly struct Box
        {
            public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

            public Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            {
                MinX = minX; MinY = minY; MinZ = minZ;
                MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
            }
        }

        public readonly struct DroppedSlice
        {
            public readonly int Part, BoxIndex;
            public readonly bool CollapsedX, CollapsedY, CollapsedZ;

            public DroppedSlice(int part, int boxIndex, bool collapsedX, bool collapsedY, bool collapsedZ)
            {
                Part = part; BoxIndex = boxIndex;
                CollapsedX = collapsedX; CollapsedY = collapsedY; CollapsedZ = collapsedZ;
            }
        }

        public sealed class Result
        {
            public BlockBox[][] PerPart = Array.Empty<BlockBox[]>();
            public int PartCount;
            public int EmptyParts;
            public bool AnchorOnly;
            public List<DroppedSlice> Dropped = new List<DroppedSlice>();
        }

        public static int Quant(float v)
        {
            int q = (int)Math.Round((double)(v * 16f));
            if (q < 0) return 0;
            if (q > 16) return 16;
            return q;
        }

        public static Result Slice(Box[] boxes, int sx, int sy, int sz)
        {
            int partCount = MultiBlock.PartCount(sx, sy, sz);
            var perPart = new BlockBox[partCount][];
            var dropped = new List<DroppedSlice>();
            int emptyParts = 0;

            for (int p = 0; p < partCount; p++)
            {
                MultiBlock.PartToLocal(p, sx, sz, out int w, out int y, out int d);
                List<BlockBox>? slices = null;
                if (boxes != null)
                {
                    for (int b = 0; b < boxes.Length; b++)
                    {
                        Box box = boxes[b];
                        float ix0 = Math.Max(box.MinX, w), ix1 = Math.Min(box.MaxX, w + 1);
                        float iy0 = Math.Max(box.MinY, y), iy1 = Math.Min(box.MaxY, y + 1);
                        float iz0 = Math.Max(box.MinZ, d), iz1 = Math.Min(box.MaxZ, d + 1);
                        if (!(ix1 > ix0 && iy1 > iy0 && iz1 > iz0))
                            continue;
                        int qx0 = Quant(ix0 - w), qx1 = Quant(ix1 - w);
                        int qy0 = Quant(iy0 - y), qy1 = Quant(iy1 - y);
                        int qz0 = Quant(iz0 - d), qz1 = Quant(iz1 - d);
                        if (qx1 > qx0 && qy1 > qy0 && qz1 > qz0)
                            (slices ??= new List<BlockBox>()).Add(
                                new BlockBox((byte)qx0, (byte)qy0, (byte)qz0, (byte)qx1, (byte)qy1, (byte)qz1));
                        else
                            dropped.Add(new DroppedSlice(p, b, qx1 <= qx0, qy1 <= qy0, qz1 <= qz0));
                    }
                }

                if (slices == null)
                {
                    perPart[p] = Array.Empty<BlockBox>();
                    emptyParts++;
                }
                else
                {
                    perPart[p] = slices.ToArray();
                }
            }

            return new Result
            {
                PerPart = perPart,
                PartCount = partCount,
                EmptyParts = emptyParts,
                AnchorOnly = partCount > 1 && emptyParts == partCount - 1 && perPart[0].Length > 0,
                Dropped = dropped
            };
        }
    }
}
