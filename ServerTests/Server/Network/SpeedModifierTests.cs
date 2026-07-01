using Server.Network;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Server.Network
{
    /// <summary>
    /// Рантайм-модификаторы скорости через скейлы AdvancedValue (ClientConnection.AddSpeedScale/RemoveSpeedScale):
    /// эффективная Speed.CurrentValue (= baseStep, едет в EntitySnapshot.Speed) масштабирует шаг. Дефолт (без
    /// модификаторов) == прежнее поведение (StepPerTick). Скейл-путь минует double-add UpdateValue(float).
    /// </summary>
    public class SpeedModifierTests
    {
        private static GridMap Corridor()
        {
            var map = new GridMap();
            for (int yy = 0; yy <= 20; yy++) map.SetTile(0, yy, 0, Tile.Floor());
            return map;
        }

        [Fact]
        public void NoModifier_SpeedEqualsStepPerTick()
        {
            var c = new ClientConnection(null!, 1);
            Assert.Equal(MovementLogic.StepPerTick, c.Speed.CurrentValue, 6); // ноль изменения поведения
        }

        [Fact]
        public void AddSpeedScale_ScalesEffectiveStep_RemoveReverts()
        {
            var map = Corridor();
            var c = new ClientConnection(null!, 1);

            // baseline без модификатора.
            float xb = 0.5f, yb = 0.5f;
            MovementLogic.Apply(map, 0, ref xb, ref yb, IntentDirection.North, sprint: false, baseStep: c.Speed.CurrentValue);
            float baseDelta = yb - 0.5f;

            // ×1.5 скейл → CurrentValue ×1.5 → шаг ×1.5.
            c.AddSpeedScale(1.5f);
            Assert.Equal(MovementLogic.StepPerTick * 1.5f, c.Speed.CurrentValue, 6);

            float xs = 0.5f, ys = 0.5f;
            MovementLogic.Apply(map, 0, ref xs, ref ys, IntentDirection.North, sprint: false, baseStep: c.Speed.CurrentValue);
            float scaledDelta = ys - 0.5f;
            Assert.True(System.MathF.Abs(scaledDelta - 1.5f * baseDelta) < 1e-5f, $"base={baseDelta}, scaled={scaledDelta}");

            // Снятие модификатора → возврат к StepPerTick.
            c.RemoveSpeedScale(1.5f);
            Assert.Equal(MovementLogic.StepPerTick, c.Speed.CurrentValue, 6);
        }
    }
}
