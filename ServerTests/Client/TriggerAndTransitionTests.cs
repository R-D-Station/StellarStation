using ZoneBox = global::Client.Gameplay.ZoneBox;
using TransitionMath = global::Client.Gameplay.TransitionMath;

namespace ServerTests.Gameplay
{
    /// <summary>Чистая логика триггер-объёма (вход/выход с гистерезисом) и переходов реакции (скорость/ускорение/лерп цвета).</summary>
    public class TriggerAndTransitionTests
    {
        private const float Cx = 0f, Cy = 0f, Cz = 0f;
        private const float Sx = 4f, Sy = 2f, Sz = 6f;
        private const float ExitMargin = 0.3f;

        private static bool Next(bool wasInside, float px, float py, float pz)
            => ZoneBox.NextInside(wasInside, px, py, pz, Cx, Cy, Cz, Sx, Sy, Sz, ExitMargin);

        [Fact]
        public void Hysteresis_EnterRequiresCoreBox_ExitRequiresLeavingMargin()
        {
            Assert.False(Next(false, 0f, 0f, 3.15f));
            Assert.True(Next(true, 0f, 0f, 3.15f));

            Assert.True(Next(false, 0f, 0f, 2.9f));
            Assert.False(Next(true, 0f, 0f, 3.4f));
        }

        [Fact]
        public void Hysteresis_UsesEachAxisHalfSize_NotACube()
        {
            Assert.True(Next(false, 1.9f, 0f, 0f));
            Assert.False(Next(false, 2.1f, 0f, 0f));

            Assert.True(Next(false, 0f, 0f, 2.9f));
            Assert.False(Next(false, 0f, 1.1f, 0f));
        }

        [Fact]
        public void Hysteresis_DeepInsideAndFarOutside_AreStable()
        {
            Assert.True(Next(false, 0f, 0f, 0f));
            Assert.True(Next(true, 0f, 0f, 0f));
            Assert.False(Next(false, 99f, 99f, 99f));
            Assert.False(Next(true, 99f, 99f, 99f));
        }

        [Fact]
        public void StepTowards_MovesBySpeedAndDoesNotOvershoot()
        {
            Assert.Equal(0.5f, TransitionMath.StepTowards(0f, 1f, 2f, 0.25f), 4);
            Assert.Equal(1f, TransitionMath.StepTowards(0.9f, 1f, 2f, 0.25f), 4);
            Assert.Equal(0f, TransitionMath.StepTowards(0.1f, 0f, 2f, 0.25f), 4);
        }

        [Fact]
        public void StepTowards_ZeroSpeed_SnapsToTarget()
            => Assert.Equal(1f, TransitionMath.StepTowards(0f, 1f, 0f, 0.016f), 4);

        [Fact]
        public void StepAccelerated_SecondStepCoversMoreThanFirst()
        {
            const float dt = 0.1f, accel = 5f;
            float p0 = TransitionMath.StepAccelerated(0f, 10f, 0f, accel, dt, out float v1);
            float p1 = TransitionMath.StepAccelerated(p0, 10f, v1, accel, dt, out _);

            float first = p0;
            float second = p1 - p0;
            Assert.True(second > first, $"ускорение обязано наращивать шаг: {first} -> {second}");
        }

        [Fact]
        public void StepAccelerated_ClampsAtTarget_AndResetsVelocity()
        {
            float p = TransitionMath.StepAccelerated(0.99f, 1f, 10f, 5f, 0.1f, out float v);
            Assert.Equal(1f, p, 4);
            Assert.Equal(0f, v, 4);
        }

        [Fact]
        public void LerpChannel_AsymmetricEndpoints_AreNotInterchangeable()
        {
            const float from = 0.2f, to = 0.9f;

            Assert.Equal(from, TransitionMath.LerpChannel(from, to, 0f), 4);
            Assert.Equal(to, TransitionMath.LerpChannel(from, to, 1f), 4);
            Assert.Equal(0.375f, TransitionMath.LerpChannel(from, to, 0.25f), 4);
            Assert.NotEqual(TransitionMath.LerpChannel(from, to, 0.25f), TransitionMath.LerpChannel(to, from, 0.25f), 4);
        }

        [Fact]
        public void LerpChannel_AlphaFadeToTransparent_GoesDown()
        {
            float quarter = TransitionMath.LerpChannel(1f, 0f, 0.25f);
            Assert.Equal(0.75f, quarter, 4);
            Assert.True(quarter < 1f, "затухание обязано УМЕНЬШАТЬ альфу, а не увеличивать");
        }

        [Fact]
        public void Clamp01_BoundsProgress()
        {
            Assert.Equal(0f, TransitionMath.Clamp01(-3f), 4);
            Assert.Equal(1f, TransitionMath.Clamp01(7f), 4);
            Assert.Equal(0.42f, TransitionMath.Clamp01(0.42f), 4);
        }
    }
}
