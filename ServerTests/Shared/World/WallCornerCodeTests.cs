using Shared.World;

namespace ServerTests.Shared.World
{
    /// <summary>WallCornerCode: основной слой `_cur` по форме; кодировщик слоя углов (все 16 масок → `_cur_corner`,`_rotate`);
    /// компенсация двойного поворота меша. Биты маски: NW=1, NE=2, SE=4, SW=8 (по часовой от NW).</summary>
    public class WallCornerCodeTests
    {
        [Theory]
        [InlineData(WallShape.Cross, 0)]
        [InlineData(WallShape.T, 1)]
        [InlineData(WallShape.Corner, 2)]
        [InlineData(WallShape.Straight, 3)]
        [InlineData(WallShape.End, 4)]
        [InlineData(WallShape.Single, 5)]
        public void CurFromShape_MatchesContract(WallShape shape, int expectedCur)
        {
            Assert.Equal((byte)expectedCur, WallCornerCode.CurFromShape(shape));
        }

        // Полная таблица: маска углов → (_cur_corner, _rotate). Покрывает все 16 комбинаций.
        [Theory]
        [InlineData(0b0000, 0, 0)] // нет углов
        [InlineData(0b0001, 1, 3)] // NW (LowestPos=0 + SingleCornerRotate=3)
        [InlineData(0b0010, 1, 0)] // NE (LowestPos=1 +3 ≡ 0)
        [InlineData(0b0100, 1, 1)] // SE (LowestPos=2 +3 ≡ 1)
        [InlineData(0b1000, 1, 2)] // SW (LowestPos=3 +3 ≡ 2)
        [InlineData(0b0011, 2, 0)] // NW+NE (смежные, ведущий NW=0 + AdjacentPairRotate=0)
        [InlineData(0b0110, 2, 1)] // NE+SE (ведущий NE=1 +0)
        [InlineData(0b1100, 2, 2)] // SE+SW (ведущий SE=2 +0)
        [InlineData(0b1001, 2, 3)] // SW+NW (ведущий SW=3 +0)
        [InlineData(0b0101, 3, 0)] // NW+SE (диагональ)
        [InlineData(0b1010, 3, 1)] // NE+SW (диагональ)
        [InlineData(0b0111, 4, 3)] // NW+NE+SE (нет SW=3)
        [InlineData(0b1011, 4, 2)] // NW+NE+SW (нет SE=2)
        [InlineData(0b1101, 4, 1)] // NW+SE+SW (нет NE=1)
        [InlineData(0b1110, 4, 0)] // NE+SE+SW (нет NW=0)
        [InlineData(0b1111, 5, 0)] // все 4
        public void EncodeCorners_MatchesTable(int mask, int expectedCorner, int expectedRotate)
        {
            var (curCorner, rotate) = WallCornerCode.EncodeCorners((byte)mask);
            Assert.Equal((byte)expectedCorner, curCorner);
            Assert.Equal((byte)expectedRotate, rotate);
        }

        [Fact]
        public void EncodeCorners_IgnoresHighBits()
        {
            // Старшие биты (>0x0F) не влияют — маска берётся по нижнему ниблу.
            Assert.Equal(WallCornerCode.EncodeCorners(0b0001), WallCornerCode.EncodeCorners(0b1111_0001 & 0xFF));
        }

        // Итог = (rotate - steps + 2) mod 4: −steps компенсирует поворот меша, +2 — полуоборот базовой ориентации спрайта.
        // Per-surface/per-shape поправка (визуальная калибровка 2026-07-07): дельта четвертей, прибавляется к _rotate в MapRenderer.
        [Theory]
        [InlineData(true, WallShape.Cross, 1, 1)]    // пол-крест 1 угол: +90°
        [InlineData(true, WallShape.Cross, 2, 0)]    // пол-крест не-1-угол: без поправки
        [InlineData(true, WallShape.Corner, 1, 1)]   // пол-угол: калибровка → _rotate=3
        [InlineData(true, WallShape.T, 1, 0)]        // пол-T: без поправки
        [InlineData(false, WallShape.Corner, 1, 1)]  // стена-угол: +90°
        [InlineData(false, WallShape.T, 1, 3)]       // стена-T (одиночный): −90°
        [InlineData(false, WallShape.T, 2, 3)]       // стена-T (смежная пара): −90°
        [InlineData(false, WallShape.Cross, 1, 1)]   // стена-крест 1 угол: +90°
        [InlineData(false, WallShape.Cross, 2, 0)]   // стена-крест не-1-угол: без поправки
        public void ShapeCornerQuarter_MatchesCalibration(bool isFloor, WallShape shape, int curCorner, int expected)
        {
            Assert.Equal(expected, WallCornerCode.ShapeCornerQuarter(isFloor, shape, (byte)curCorner));
        }

        // Итог = (rotate - steps + 2) mod 4: −steps компенсирует поворот меша, +2 — полуоборот базовой ориентации спрайта.
        [Theory]
        [InlineData(0, 0, 2)] // 0-0+2
        [InlineData(1, 0, 3)] // 1-0+2
        [InlineData(0, 1, 1)] // 0-1+2
        [InlineData(2, 1, 3)] // 2-1+2
        [InlineData(3, 1, 0)] // 3-1+2=4≡0
        [InlineData(1, 3, 0)] // 1-3+2=0
        [InlineData(0, 4, 2)] // 0-4+2=-2≡2
        public void CompensateRotateForMesh_AppliesHalfTurnMinusSteps(int rotate, int steps, int expected)
        {
            Assert.Equal((byte)expected, WallCornerCode.CompensateRotateForMesh((byte)rotate, steps));
        }
    }
}
