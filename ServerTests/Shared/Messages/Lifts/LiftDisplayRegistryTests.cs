using System.Collections.Generic;
using Shared.Messages.Lifts;

namespace ServerTests.Shared.Messages.Lifts
{
    /// <summary>Реестр дисплеев по клетке двери: дисплей появляется вместе с блоком (стрим/пул),
    /// поэтому регистрация обязана сразу получить актуальное состояние, а мёртвые — отваливаться.</summary>
    public class LiftDisplayRegistryTests
    {
        private sealed class FakeDisplay : ILiftDoorDisplay
        {
            public readonly List<LiftDoorDisplayState> Applied = new List<LiftDoorDisplayState>();
            public bool IsAlive { get; set; } = true;
            public void Apply(in LiftDoorDisplayState state) => Applied.Add(state);
        }

        private const long CellA = 12345L;
        private const long CellB = 67890L;

        private static LiftDoorDisplayState State(int floor, int dir = 0, bool called = false)
            => new LiftDoorDisplayState(floor, dir, called, true);

        [Fact]
        public void Register_AfterPush_GetsCurrentStateImmediately()
        {
            // Дверь приезжает со стримом посреди движения кабины: без толчка она показывала бы
            // пустоту до следующей смены этажа.
            var registry = new LiftDisplayRegistry();
            registry.Push(CellA, State(5, 1));

            var display = new FakeDisplay();
            registry.Register(CellA, display);

            Assert.Equal(State(5, 1), Assert.Single(display.Applied));
        }

        [Fact]
        public void Register_WithoutAnyPush_StaysSilent()
        {
            var registry = new LiftDisplayRegistry();
            var display = new FakeDisplay();
            registry.Register(CellA, display);
            Assert.Empty(display.Applied);
        }

        [Fact]
        public void Push_ReachesOnlyItsOwnCell()
        {
            var registry = new LiftDisplayRegistry();
            var a = new FakeDisplay();
            var b = new FakeDisplay();
            registry.Register(CellA, a);
            registry.Register(CellB, b);

            registry.Push(CellA, State(3));

            Assert.Single(a.Applied);
            Assert.Empty(b.Applied);
        }

        [Fact]
        public void Push_OfUnchangedState_DoesNotReapply()
        {
            var registry = new LiftDisplayRegistry();
            var display = new FakeDisplay();
            registry.Register(CellA, display);

            registry.Push(CellA, State(3));
            registry.Push(CellA, State(3));
            registry.Push(CellA, State(3));
            Assert.Single(display.Applied);

            registry.Push(CellA, State(4));
            Assert.Equal(2, display.Applied.Count);
        }

        [Fact]
        public void DuplicateRegister_DoesNotDoubleTheDisplay()
        {
            var registry = new LiftDisplayRegistry();
            var display = new FakeDisplay();
            registry.Register(CellA, display);
            registry.Register(CellA, display);

            Assert.Equal(1, registry.DisplayCount(CellA));
            registry.Push(CellA, State(3));
            Assert.Single(display.Applied);
        }

        [Fact]
        public void SeveralDisplays_OnOneCell_AllGetUpdated()
        {
            var registry = new LiftDisplayRegistry();
            var a = new FakeDisplay();
            var b = new FakeDisplay();
            registry.Register(CellA, a);
            registry.Register(CellA, b);

            registry.Push(CellA, State(7, -1));

            Assert.Equal(2, registry.DisplayCount(CellA));
            Assert.Single(a.Applied);
            Assert.Single(b.Applied);
        }

        [Fact]
        public void Unregister_StopsUpdates_AndSurvivesUnknownArguments()
        {
            var registry = new LiftDisplayRegistry();
            var display = new FakeDisplay();
            registry.Register(CellA, display);
            registry.Unregister(CellA, display);

            registry.Unregister(CellA, display);
            registry.Unregister(CellB, display);
            registry.Unregister(CellA, null);

            registry.Push(CellA, State(3));
            Assert.Empty(display.Applied);
            Assert.Equal(0, registry.DisplayCount(CellA));
        }

        [Fact]
        public void Unregister_LeavesOtherDisplaysOnTheSameCell()
        {
            var registry = new LiftDisplayRegistry();
            var a = new FakeDisplay();
            var b = new FakeDisplay();
            registry.Register(CellA, a);
            registry.Register(CellA, b);
            registry.Unregister(CellA, a);

            registry.Push(CellA, State(3));

            Assert.Empty(a.Applied);
            Assert.Single(b.Applied);
        }

        [Fact]
        public void DeadDisplay_IsDropped_AndDoesNotBlockLiveOnes()
        {
            // Визуал блока может умереть без OnDespawn (выгрузка сцены) — реестр обязан это пережить.
            var registry = new LiftDisplayRegistry();
            var dead = new FakeDisplay();
            var live = new FakeDisplay();
            registry.Register(CellA, dead);
            registry.Register(CellA, live);
            dead.IsAlive = false;

            registry.Push(CellA, State(3));

            Assert.Empty(dead.Applied);
            Assert.Single(live.Applied);
            Assert.Equal(1, registry.DisplayCount(CellA));
        }

        [Fact]
        public void StateIsCached_EvenWithNobodyListening()
        {
            var registry = new LiftDisplayRegistry();
            registry.Push(CellA, State(9, 1, called: true));

            Assert.True(registry.TryGetLast(CellA, out var cached));
            Assert.Equal(State(9, 1, called: true), cached);
            Assert.False(registry.TryGetLast(CellB, out _));
        }

        [Fact]
        public void ReRegisterAfterUnregister_GetsTheLatestState()
        {
            // Ровно путь пула: блок ушёл в пул, вернулся на ту же клетку и обязан догнать состояние.
            var registry = new LiftDisplayRegistry();
            var display = new FakeDisplay();
            registry.Register(CellA, display);
            registry.Push(CellA, State(2));
            registry.Unregister(CellA, display);
            registry.Push(CellA, State(6, 1));

            registry.Register(CellA, display);

            Assert.Equal(2, display.Applied.Count);
            Assert.Equal(State(6, 1), display.Applied[1]);
        }
    }
}
