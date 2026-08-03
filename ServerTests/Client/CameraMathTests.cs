using CameraMath = global::Client.Gameplay.Camera.CameraMath;
using ZoneBox = global::Client.Gameplay.ZoneBox;
using CameraQuadrant = global::Client.Gameplay.Camera.CameraQuadrant;
using IntentDirection = global::Shared.Messages.Core.IntentDirection;

namespace ServerTests.Gameplay
{
    /// <summary>Чистая математика камеры: поворот офсета по четвертям, выбор зоны при перекрытии, точка в повёрнутом боксе.</summary>
    public class CameraMathTests
    {
        private const float BaseX = 1.5f, BaseY = 3f, BaseZ = -2.5f;

        [Theory]
        [InlineData(0, BaseX, BaseZ)]
        [InlineData(1, BaseZ, -BaseX)]
        [InlineData(2, -BaseX, -BaseZ)]
        [InlineData(3, -BaseZ, BaseX)]
        public void RotateOffset_AsymmetricBase_MatchesQuadrant(int quadrant, float expectX, float expectZ)
        {
            CameraMath.RotateOffset(BaseX, BaseY, BaseZ, quadrant, out float rx, out float ry, out float rz);

            Assert.Equal(expectX, rx, 4);
            Assert.Equal(expectZ, rz, 4);
            Assert.Equal(BaseY, ry, 4);
        }

        [Fact]
        public void RotateOffset_QuadrantsAreDistinct_XAndZNotInterchangeable()
        {
            CameraMath.RotateOffset(BaseX, BaseY, BaseZ, 1, out float x1, out _, out float z1);
            CameraMath.RotateOffset(BaseX, BaseY, BaseZ, 3, out float x3, out _, out float z3);

            Assert.NotEqual(x1, z1, 4);
            Assert.NotEqual(x1, x3, 4);
            Assert.NotEqual(z1, z3, 4);
        }

        [Fact]
        public void RotateOffset_FullCircle_ReturnsOriginal()
        {
            float x = BaseX, y = BaseY, z = BaseZ;
            for (int i = 0; i < 4; i++)
            {
                CameraMath.RotateOffset(x, y, z, 1, out float rx, out float ry, out float rz);
                x = rx; y = ry; z = rz;
            }

            Assert.Equal(BaseX, x, 4);
            Assert.Equal(BaseY, y, 4);
            Assert.Equal(BaseZ, z, 4);
        }

        [Fact]
        public void Yaw_FollowsQuadrant_AndWrapsEveryFour()
        {
            Assert.Equal(0f, CameraMath.YawDegrees(0));
            Assert.Equal(90f, CameraMath.YawDegrees(1));
            Assert.Equal(180f, CameraMath.YawDegrees(2));
            Assert.Equal(270f, CameraMath.YawDegrees(3));
            Assert.Equal(0, CameraMath.NextQuadrant(3));
        }

        private readonly struct Zone
        {
            public readonly int Priority;
            public readonly float Volume;
            public readonly int Order;
            public readonly string Name;

            public Zone(string name, int priority, float volume, int order)
            {
                Name = name; Priority = priority; Volume = volume; Order = order;
            }
        }

        private static string Best(params Zone[] zones)
        {
            Zone best = zones[0];
            for (int i = 1; i < zones.Length; i++)
                if (CameraMath.IsBetter(zones[i].Priority, zones[i].Volume, zones[i].Order,
                                        best.Priority, best.Volume, best.Order))
                    best = zones[i];
            return best.Name;
        }

        [Fact]
        public void ZoneSelect_HigherPriorityWins_RegardlessOfOrder()
        {
            var low = new Zone("low", 0, 1f, 0);
            var high = new Zone("high", 5, 999f, 1);

            Assert.Equal("high", Best(low, high));
            Assert.Equal("high", Best(high, low));
        }

        [Fact]
        public void ZoneSelect_EqualPriority_SmallerVolumeWins_RegardlessOfOrder()
        {
            var big = new Zone("big", 2, 100f, 0);
            var small = new Zone("small", 2, 10f, 1);

            Assert.Equal("small", Best(big, small));
            Assert.Equal("small", Best(small, big));
        }

        [Fact]
        public void ZoneSelect_FullTie_ResolvedByRegistrationOrder_Deterministic()
        {
            var first = new Zone("first", 1, 8f, 0);
            var second = new Zone("second", 1, 8f, 1);

            Assert.Equal("first", Best(first, second));
            Assert.Equal("first", Best(second, first));
        }

        [Fact]
        public void ZoneSelect_IsBetter_IsAntisymmetric()
        {
            bool ab = CameraMath.IsBetter(1, 5f, 0, 1, 5f, 1);
            bool ba = CameraMath.IsBetter(1, 5f, 1, 1, 5f, 0);

            Assert.True(ab);
            Assert.False(ba);
            Assert.False(CameraMath.IsBetter(1, 5f, 0, 1, 5f, 0));
        }

        private static bool ContainsWorld(float px, float pz, float originX, float originZ, int quadrant,
                                          float sizeX, float sizeZ, float margin = 0f)
        {
            int inverse = (4 - (quadrant & 3)) & 3;
            CameraMath.RotateOffset(px - originX, 0f, pz - originZ, inverse,
                                    out float lx, out float ly, out float lz);
            return ZoneBox.ContainsLocal(lx, ly, lz, 0f, 0f, 0f, sizeX, 2f, sizeZ, margin);
        }

        [Fact]
        public void RotatedBox_LongAxisFollowsRotation()
        {
            const float ox = 10f, oz = 5f;
            const float sizeX = 4f, sizeZ = 2f;

            Assert.True(ContainsWorld(ox + 1.8f, oz, ox, oz, 0, sizeX, sizeZ));
            Assert.False(ContainsWorld(ox, oz + 1.8f, ox, oz, 0, sizeX, sizeZ));

            Assert.False(ContainsWorld(ox + 1.8f, oz, ox, oz, 1, sizeX, sizeZ));
            Assert.True(ContainsWorld(ox, oz + 1.8f, ox, oz, 1, sizeX, sizeZ));
        }

        [Fact]
        public void ContainsLocal_MarginExpandsBox_AndNegativeSizeIsAbsolute()
        {
            Assert.False(ZoneBox.ContainsLocal(0f, 0f, 1.2f, 0f, 0f, 0f, 2f, 2f, 2f, 0f));
            Assert.True(ZoneBox.ContainsLocal(0f, 0f, 1.2f, 0f, 0f, 0f, 2f, 2f, 2f, 0.3f));
            Assert.True(ZoneBox.ContainsLocal(0f, 0f, 0.9f, 0f, 0f, 0f, -2f, -2f, -2f, 0f));
        }

        [Fact]
        public void ContainsLocal_RespectsOffsetCenter()
        {
            Assert.True(ZoneBox.ContainsLocal(5f, 0f, 0f, 5f, 0f, 0f, 2f, 2f, 2f, 0f));
            Assert.False(ZoneBox.ContainsLocal(0f, 0f, 0f, 5f, 0f, 0f, 2f, 2f, 2f, 0f));
        }

        [Fact]
        public void BoxVolume_UsesAbsoluteSizes()
        {
            Assert.Equal(24f, ZoneBox.Volume(2f, 3f, 4f), 4);
            Assert.Equal(24f, ZoneBox.Volume(-2f, 3f, -4f), 4);
        }

        [Theory]
        [InlineData(IntentDirection.North, IntentDirection.North, IntentDirection.East, IntentDirection.South, IntentDirection.West)]
        [InlineData(IntentDirection.East, IntentDirection.East, IntentDirection.South, IntentDirection.West, IntentDirection.North)]
        [InlineData(IntentDirection.South, IntentDirection.South, IntentDirection.West, IntentDirection.North, IntentDirection.East)]
        [InlineData(IntentDirection.West, IntentDirection.West, IntentDirection.North, IntentDirection.East, IntentDirection.South)]
        [InlineData(IntentDirection.NorthEast, IntentDirection.NorthEast, IntentDirection.SouthEast, IntentDirection.SouthWest, IntentDirection.NorthWest)]
        [InlineData(IntentDirection.SouthEast, IntentDirection.SouthEast, IntentDirection.SouthWest, IntentDirection.NorthWest, IntentDirection.NorthEast)]
        [InlineData(IntentDirection.SouthWest, IntentDirection.SouthWest, IntentDirection.NorthWest, IntentDirection.NorthEast, IntentDirection.SouthEast)]
        [InlineData(IntentDirection.NorthWest, IntentDirection.NorthWest, IntentDirection.NorthEast, IntentDirection.SouthEast, IntentDirection.SouthWest)]
        public void RotateIntent_EachQuadrant_StepsClockwise(IntentDirection dir,
            IntentDirection q0, IntentDirection q1, IntentDirection q2, IntentDirection q3)
        {
            Assert.Equal(q0, CameraMath.RotateIntent(dir, 0));
            Assert.Equal(q1, CameraMath.RotateIntent(dir, 1));
            Assert.Equal(q2, CameraMath.RotateIntent(dir, 2));
            Assert.Equal(q3, CameraMath.RotateIntent(dir, 3));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void RotateIntent_None_StaysNone(int quadrant)
            => Assert.Equal(IntentDirection.None, CameraMath.RotateIntent(IntentDirection.None, quadrant));

        [Theory]
        [InlineData(IntentDirection.North)]
        [InlineData(IntentDirection.East)]
        [InlineData(IntentDirection.South)]
        [InlineData(IntentDirection.West)]
        [InlineData(IntentDirection.NorthEast)]
        [InlineData(IntentDirection.SouthWest)]
        public void RotateIntent_FullCircle_ReturnsOriginal(IntentDirection dir)
        {
            IntentDirection d = dir;
            for (int i = 0; i < 4; i++)
                d = CameraMath.RotateIntent(d, 1);

            Assert.Equal(dir, d);
        }

        [Fact]
        public void RotateIntent_QuadrantOne_IsNotSymmetric_EastWestDistinguished()
        {
            Assert.Equal(IntentDirection.East, CameraMath.RotateIntent(IntentDirection.North, 1));
            Assert.NotEqual(IntentDirection.West, CameraMath.RotateIntent(IntentDirection.North, 1));
            Assert.Equal(IntentDirection.West, CameraMath.RotateIntent(IntentDirection.South, 1));
        }

        [Fact]
        public void Quadrant_SingleSource_DrivesYawOffsetAndInputTogether()
        {
            var q = new CameraQuadrant();
            Assert.Equal(0, q.Value);

            for (int step = 1; step <= 4; step++)
            {
                q.Next();

                Assert.Equal(CameraMath.YawDegrees(q.Value), q.Yaw);
                Assert.Equal(CameraMath.RotateIntent(IntentDirection.North, q.Value), q.Rotate(IntentDirection.North));

                CameraMath.RotateOffset(BaseX, BaseY, BaseZ, q.Value, out float ex, out float ey, out float ez);
                q.RotateOffset(BaseX, BaseY, BaseZ, out float ax, out float ay, out float az);
                Assert.Equal(ex, ax, 4);
                Assert.Equal(ey, ay, 4);
                Assert.Equal(ez, az, 4);
            }

            Assert.Equal(0, q.Value);
        }

        [Fact]
        public void Quadrant_YawAndInputAgree_OnCameraForward()
        {
            var q = new CameraQuadrant();

            Assert.Equal(0f, q.Yaw);
            Assert.Equal(IntentDirection.North, q.Rotate(IntentDirection.North));

            q.Next();
            Assert.Equal(90f, q.Yaw);
            Assert.Equal(IntentDirection.East, q.Rotate(IntentDirection.North));

            q.Next();
            Assert.Equal(180f, q.Yaw);
            Assert.Equal(IntentDirection.South, q.Rotate(IntentDirection.North));

            q.Next();
            Assert.Equal(270f, q.Yaw);
            Assert.Equal(IntentDirection.West, q.Rotate(IntentDirection.North));
        }
    }
}
