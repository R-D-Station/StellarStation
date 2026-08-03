using System.Linq;
using Shared.World.Blocks;
using Xunit;

namespace ServerTests.Shared.World.Blocks
{
    public class MultiBlockSlicerTests
    {
        private static MultiBlockSlicer.Box Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            => new MultiBlockSlicer.Box(minX, minY, minZ, maxX, maxY, maxZ);

        [Fact]
        public void FullBox_5x5x1_AllPartsFullCell()
        {
            var r = MultiBlockSlicer.Slice(new[] { Box(0, 0, 0, 5, 5, 1) }, 5, 5, 1);

            Assert.Equal(25, r.PartCount);
            Assert.Equal(0, r.EmptyParts);
            Assert.Empty(r.Dropped);
            for (int p = 0; p < r.PartCount; p++)
            {
                Assert.Single(r.PerPart[p]);
                var b = r.PerPart[p][0];
                Assert.Equal((byte)0, b.MinX); Assert.Equal((byte)0, b.MinY); Assert.Equal((byte)0, b.MinZ);
                Assert.Equal((byte)16, b.MaxX); Assert.Equal((byte)16, b.MaxY); Assert.Equal((byte)16, b.MaxZ);
            }
        }

        [Fact]
        public void AnchorOnlyBox_5x5x1_LeavesTwentyFourEmpty()
        {
            var r = MultiBlockSlicer.Slice(new[] { Box(0, 0, 0, 1, 1, 1) }, 5, 5, 1);

            Assert.Equal(25, r.PartCount);
            Assert.Equal(24, r.EmptyParts);
            Assert.True(r.AnchorOnly);
            Assert.Single(r.PerPart[0]);
            for (int p = 1; p < r.PartCount; p++)
                Assert.Empty(r.PerPart[p]);
        }

        [Fact]
        public void SliverIntoNeighbour_Dropped_AndReportedInDiagnostics()
        {
            var r = MultiBlockSlicer.Slice(new[] { Box(0, 0, 0, 1f + 1f / 64f, 1, 1) }, 2, 1, 1);

            Assert.Single(r.PerPart[0]);
            Assert.Empty(r.PerPart[1]);
            Assert.Contains(r.Dropped, d => d.Part == 1 && d.CollapsedX);
        }

        [Fact]
        public void UnalignedLipAcrossFiveCells_InternalBoundariesExact_NoGaps()
        {
            var r = MultiBlockSlicer.Slice(new[] { Box(0, 0, 0, 5, 0.2f, 1) }, 5, 1, 1);

            Assert.Equal(5, r.PartCount);
            Assert.Equal(0, r.EmptyParts);
            for (int p = 0; p < 5; p++)
            {
                var b = r.PerPart[p][0];
                Assert.Equal((byte)0, b.MinX);
                Assert.Equal((byte)16, b.MaxX);
                Assert.Equal((byte)0, b.MinY);
                Assert.Equal((byte)3, b.MaxY);
            }
        }

        [Fact]
        public void FullBox_5x5x5_AllHundredTwentyFivePartsFullCell()
        {
            var r = MultiBlockSlicer.Slice(new[] { Box(0, 0, 0, 5, 5, 5) }, 5, 5, 5);

            Assert.Equal(125, r.PartCount);
            Assert.Equal(0, r.EmptyParts);
            Assert.True(r.PerPart.All(part => part.Length == 1
                && part[0].MinX == 0 && part[0].MaxX == 16
                && part[0].MinY == 0 && part[0].MaxY == 16
                && part[0].MinZ == 0 && part[0].MaxZ == 16));
        }
    }
}
