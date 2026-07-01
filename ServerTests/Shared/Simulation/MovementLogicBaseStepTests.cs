using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    /// <summary>MovementLogic.Apply с параметром baseStep: дефолт == прежнее поведение (StepPerTick), масштаб
    /// базы масштабирует шаг линейно. baseStep идёт из AdvancedValue.CurrentValue (одна база обе стороны).</summary>
    public class MovementLogicBaseStepTests
    {
        private static GridMap Corridor()
        {
            var map = new GridMap();
            for (int yy = 0; yy <= 20; yy++) map.SetTile(0, yy, 0, Tile.Floor());
            return map;
        }

        [Fact]
        public void DefaultBaseStep_MatchesStepPerTick_ZeroBehaviourChange()
        {
            var map = Corridor();

            float x = 0.5f, y = 0.5f;
            MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, sprint: false); // дефолтный baseStep
            float deltaDefault = y - 0.5f;

            float x2 = 0.5f, y2 = 0.5f;
            MovementLogic.Apply(map, 0, ref x2, ref y2, IntentDirection.North, sprint: false,
                                crawl: false, baseStep: MovementLogic.StepPerTick); // явный StepPerTick
            float deltaExplicit = y2 - 0.5f;

            Assert.Equal(deltaExplicit, deltaDefault, 6);                          // дефолт == явный StepPerTick
            Assert.True(System.MathF.Abs(deltaDefault - MovementLogic.StepPerTick) < 1e-5f, $"delta={deltaDefault}");
        }

        [Fact]
        public void DoubledBaseStep_DoublesStep()
        {
            var map = Corridor();

            float x = 0.5f, y = 0.5f;
            MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, sprint: false, baseStep: MovementLogic.StepPerTick);
            float baseDelta = y - 0.5f;

            float x2 = 0.5f, y2 = 0.5f;
            MovementLogic.Apply(map, 0, ref x2, ref y2, IntentDirection.North, sprint: false, baseStep: 2f * MovementLogic.StepPerTick);
            float doubleDelta = y2 - 0.5f;

            Assert.True(System.MathF.Abs(doubleDelta - 2f * baseDelta) < 1e-5f, $"base={baseDelta}, double={doubleDelta}");
        }
    }
}
