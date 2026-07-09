using System.Collections.Generic;
using Shared.World;

namespace ServerTests.Shared.World
{
    /// <summary>NetIdAllocator: старт с 1, монотонность, уникальность, без переиспользования id.</summary>
    public class NetIdAllocatorTests
    {
        [Fact]
        public void Allocate_StartsAtOne()
        {
            var a = new NetIdAllocator();
            Assert.Equal(1, a.Allocate());
        }

        [Fact]
        public void Allocate_IsMonotonicAndContiguous()
        {
            var a = new NetIdAllocator();
            Assert.Equal(1, a.Allocate());
            Assert.Equal(2, a.Allocate());
            Assert.Equal(3, a.Allocate());
        }

        [Fact]
        public void Allocate_ManyIds_AllUniqueAndStrictlyIncreasing()
        {
            var a = new NetIdAllocator();
            var seen = new HashSet<int>();
            int prev = 0;
            for (int i = 0; i < 10_000; i++)
            {
                int id = a.Allocate();
                Assert.True(id > prev, "монотонно возрастает");
                Assert.True(seen.Add(id), "уникален (без переиспользования)");
                prev = id;
            }
        }

        [Fact]
        public void Allocate_IndependentInstances_DoNotShareCounter()
        {
            var a = new NetIdAllocator();
            var b = new NetIdAllocator();
            a.Allocate(); a.Allocate(); // a → 3 следующий
            Assert.Equal(1, b.Allocate()); // b — свой пул, старт с 1
        }
    }
}
