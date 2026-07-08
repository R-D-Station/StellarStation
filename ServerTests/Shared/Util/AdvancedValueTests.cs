using Shared.Util;

namespace ServerTests.Shared.Util
{
    /// <summary>AdvancedValue после порта в Shared (pure C#): математика, кламп, защита от min>max, операторы.</summary>
    public class AdvancedValueTests
    {
        [Fact]
        public void Default_CurrentValue_EqualsBase()
        {
            // MVP-конструкция скорости: CurrentValue == StepPerTick (ноль изменения поведения).
            var v = new AdvancedValue(0.1f);
            Assert.Equal(0.1f, v.CurrentValue, 6);
        }

        [Fact]
        public void Formula_BaseScaleScalesSum_ComputesCorrectly()
        {
            // (sum + base·scaleBase)·scaleCurrent·∏scales.
            var v = new AdvancedValue(baseValue: 2f, scaleBaseValue: 3f, scaleCurrentValue: 2f, minValue: 0f);
            Assert.Equal(12f, v.CurrentValue, 5);    // (0 + 2·3)·2·1 = 12
            v.AddScaleSum(0.5f);                      // ∏scales = 1·0.5
            Assert.Equal(6f, v.CurrentValue, 5);      // (0 + 6)·2·0.5 = 6
        }

        [Fact]
        public void Clamp_RespectsMinAndMax()
        {
            var hi = new AdvancedValue(baseValue: 100f, maxValue: 10f, minValue: 0f);
            Assert.Equal(10f, hi.CurrentValue, 5);    // 100 → clamp Max 10
            var lo = new AdvancedValue(baseValue: 0.01f, minValue: 0.1f);
            Assert.Equal(0.1f, lo.CurrentValue, 5);   // 0.01 → clamp Min 0.1
        }

        [Fact]
        public void InvertedBounds_DoesNotThrow_PinsToMax()
        {
            // System.Math.Clamp бросил бы при min>max; SafeClamp пинит к Max без исключения.
            var v = new AdvancedValue(baseValue: 5f); // MinValue=0.1 по умолчанию
            v.MaxValue = 0.05f;                        // Max < Min
            var ex = Record.Exception(() => v.UpdateValue());
            Assert.Null(ex);
            Assert.Equal(0.05f, v.CurrentValue, 5);
        }

        [Fact]
        public void Operators_UseCurrentValue_BothDirections()
        {
            var v = new AdvancedValue(2f, minValue: 0f);
            Assert.Equal(5f, v + 3f, 5);
            Assert.Equal(6f, v * 3f, 5);
            Assert.Equal(5f, 3f + v, 5);
            Assert.Equal(6f, 3f * v, 5);
        }

        [Fact]
        public void UpdateScaleBaseValue_RecomputesCurrent()
        {
            var v = new AdvancedValue(baseValue: 10f, minValue: 0f); // 10
            v.UpdateScaleBaseValue(1f);                              // ScaleBase = 2 → (0 + 10·2)·1·1 = 20
            Assert.Equal(20f, v.CurrentValue, 5);
        }

        [Fact]
        public void UpdateValue_Parametric_CharacterizesIntentionalDoubleAdd()
        {
            // ХАРАКТЕРИЗАЦИОННЫЙ тест: фиксирует НАМЕРЕННОЕ поведение UpdateValue(float) — double-add в else-ветке
            // СОЗНАТЕЛЬНО оставлен при порте 1-в-1 (исходный «удобный расчёт» пользователя). Рантайм-модификаторы
            // скорости идут через СКЕЙЛЫ (AddScaleSum/UpdateValue() без аргумента) и этот путь НЕ задевают.
            var v = new AdvancedValue(baseValue: 0f, minValue: 0f);
            v.UpdateValue(5f);
            Assert.Equal(5f, v.CurrentValue, 5);   // CurrentValue по Sum с ОДНИМ добавлением (5)
            v.UpdateValue(0f);
            Assert.Equal(10f, v.CurrentValue, 5);  // аккумулятор уже 10 (double-add) → проявляется на пересчёте
        }
    }
}
