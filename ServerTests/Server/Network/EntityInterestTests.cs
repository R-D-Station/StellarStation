using Server.Network;
using Shared.Messages.Core;

namespace ServerTests.Server.Network
{
    /// <summary>
    /// 2.5-D entity-PVS: юнит на предикат интереса GameServer.InInterest (spatial + Z-окно). Self-случай (e.NetId ==
    /// recipient) в предикат НЕ входит — он OR-ится у вызывающего (BroadcastWorldSnapshot) ДО InInterest, поэтому self
    /// включён ВСЕГДА, даже вне радиуса. Здесь тестируем чистую пространственную часть.
    /// </summary>
    public class EntityInterestTests
    {
        private static EntitySnapshot At(int netId, float x, float y, float z) =>
            new EntitySnapshot { NetId = netId, X = x, Y = y, Z = z };

        [Fact]
        public void Near_SameFloor_InInterest()
        {
            var e = At(2, 5f, 5f, 0f); // dist²=50 ≤ 40²=1600
            Assert.True(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 0));
        }

        [Fact]
        public void Far_BeyondRadius_NotInInterest()
        {
            var e = At(2, 100f, 0f, 0f); // dist²=10000 > 1600
            Assert.False(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 0));
        }

        [Fact]
        public void DifferentFloor_BeyondZDepth_NotInInterest()
        {
            var e = At(2, 1f, 1f, 1f); // рядом по XY, но |dz|=1 > zDepth=0
            Assert.False(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 0));
        }

        [Fact]
        public void AdjacentFloor_WithinZDepth_InInterest()
        {
            var e = At(2, 1f, 1f, 1f); // |dz|=1 ≤ zDepth=1 И в радиусе
            Assert.True(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 1));
        }

        [Fact]
        public void ExactlyOnRadiusBoundary_Included() // документируем: граница включена (<=)
        {
            var e = At(2, 40f, 0f, 0f); // dist²=1600 == R²=1600
            Assert.True(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 0));
        }

        [Fact]
        public void JustBeyondRadiusBoundary_Excluded()
        {
            var e = At(2, 41f, 0f, 0f); // dist²=1681 > 1600
            Assert.False(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 0));
        }

        [Fact]
        public void NegativeFloorDelta_WithinZDepth_Respected()
        {
            var e = At(2, 0f, 0f, -1f); // dz=-1, |dz|=1 > zDepth=0 → вне
            Assert.False(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 0));
            Assert.True(GameServer.InInterest(0f, 0f, 0, e, radius: 40f, zDepth: 1)); // depth=1 → в окне
        }

        [Fact]
        public void Self_FarAway_PredicateFalse_ButCallSiteIncludesViaNetIdOr()
        {
            // InInterest НЕ знает про self (не проверяет NetId) → дальний self предикатом отсеивается. В реальном пути
            // BroadcastWorldSnapshot добавляет `e.NetId == client.PlayerNetId ||` ПЕРЕД InInterest → self всегда в снапшоте.
            var self = At(1, 9999f, 9999f, 5f);
            Assert.False(GameServer.InInterest(0f, 0f, 0, self, radius: 40f, zDepth: 0));
        }
    }
}
