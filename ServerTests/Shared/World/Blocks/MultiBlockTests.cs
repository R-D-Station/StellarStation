using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class MultiBlockTests
    {
        [Fact]
        public void PartLocal_RoundTrips()
        {
            // Дверь 2×1×2 (sx=2, sy=2? нет: ширина 2, глубина 1, высота 2 → sx=2, sz=1, sy=2): 4 части.
            for (int part = 0; part < 4; part++)
            {
                MultiBlock.PartToLocal(part, 2, 1, out int w, out int y, out int d);
                Assert.Equal(part, MultiBlock.LocalToPart(w, y, d, 2, 2, 1));
            }
        }

        [Fact]
        public void Rotation_ClockwiseSteps()
        {
            MultiBlock.RotateLocal(1, 0, 0, out int dx, out int dz);
            Assert.Equal((1, 0), (dx, dz));  // север: ширина → +X
            MultiBlock.RotateLocal(1, 0, 1, out dx, out dz);
            Assert.Equal((0, -1), (dx, dz)); // восток: по часовой
            MultiBlock.RotateLocal(1, 0, 2, out dx, out dz);
            Assert.Equal((-1, 0), (dx, dz));
            MultiBlock.RotateLocal(1, 0, 3, out dx, out dz);
            Assert.Equal((0, 1), (dx, dz));
        }

        [Fact]
        public void Anchor_RecoveredFromAnyPart()
        {
            const int sx = 2, sz = 1;
            foreach (int facing in new[] { 0, 1, 2, 3 })
                for (int part = 0; part < MultiBlock.PartCount(sx, 2, sz); part++)
                {
                    MultiBlock.PartWorldOffset(part, sx, sz, facing, out int dx, out int dy, out int dz);
                    MultiBlock.AnchorOf(10 + dx, 5 + dy, -3 + dz, part, sx, sz, facing,
                                        out int ax, out int ay, out int az);
                    Assert.Equal((10, 5, -3), (ax, ay, az));
                }
        }
    }
}
