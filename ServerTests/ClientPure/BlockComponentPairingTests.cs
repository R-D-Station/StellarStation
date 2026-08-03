using Client.Lifts;

namespace ServerTests.ClientPure
{
    /// <summary>Парность OnSpawn/OnDespawn у визуала ВНЕ пула блоков. Компоненты кабины регистрируются
    /// в своих реестрах (TriggerZone, CameraZone), поэтому лишний Spawn или пропущенный Despawn = зона,
    /// срабатывающая после исчезновения лифта. Реестры Unity-типизированы и headless недоступны —
    /// проверяется дисциплина, которая ими управляет.</summary>
    public class BlockComponentPairingTests
    {
        [Fact]
        public void FreshVisual_HasNothingToDespawn()
        {
            var p = new BlockComponentPairing();

            Assert.False(p.Spawned);
            Assert.False(p.BeginDespawn());
            Assert.Equal(0, p.Outstanding);
        }

        [Fact]
        public void Spawn_IsIssuedOnce_EvenIfAskedEveryFrame()
        {
            var p = new BlockComponentPairing();

            Assert.True(p.BeginSpawn());
            Assert.False(p.BeginSpawn());
            Assert.False(p.BeginSpawn());
            Assert.Equal(1, p.Outstanding);
        }

        [Fact]
        public void Despawn_IsIssuedOnce_AndClosesTheSpawn()
        {
            var p = new BlockComponentPairing();
            p.BeginSpawn();

            Assert.True(p.BeginDespawn());
            Assert.False(p.BeginDespawn());
            Assert.Equal(0, p.Outstanding);
            Assert.False(p.Spawned);
        }

        [Fact]
        public void Rebuild_ClosesTheOldSpawn_BeforeOpeningTheNew()
        {
            // Пересборка вьюхи (смена CabinDefId/плана) — Despawn старых + Spawn новых. Пропусти Despawn,
            // и старая зона осталась бы в реестре навсегда, а Outstanding уехал бы в 2.
            var p = new BlockComponentPairing();
            p.BeginSpawn();

            Assert.True(p.BeginDespawn());
            Assert.True(p.BeginSpawn());
            Assert.Equal(1, p.Outstanding);
        }

        [Fact]
        public void LiftDisappears_LeavesNothingRegistered()
        {
            // «Лифт исчез → зон в реестре не осталось»: сколько бы пересборок ни было, финальный Destroy
            // обязан свести баланс в ноль.
            var p = new BlockComponentPairing();

            for (int i = 0; i < 5; i++)
            {
                p.BeginDespawn();
                p.BeginSpawn();
            }
            p.BeginDespawn();

            Assert.Equal(0, p.Outstanding);
            Assert.False(p.Spawned);
        }

        [Fact]
        public void DestroyTwice_DoesNotDoubleUnregister()
        {
            // Destroy зовётся и из Rebuild, и из метлы осиротевших визуалов — второй раз обязан быть no-op,
            // иначе чужой объект вылетел бы из реестра.
            var p = new BlockComponentPairing();
            p.BeginSpawn();
            p.BeginDespawn();

            Assert.False(p.BeginDespawn());
            Assert.Equal(0, p.Outstanding);
        }

        [Fact]
        public void OutstandingNeverGoesNegative()
        {
            var p = new BlockComponentPairing();
            for (int i = 0; i < 4; i++)
                p.BeginDespawn();

            Assert.Equal(0, p.Outstanding);
        }
    }
}
