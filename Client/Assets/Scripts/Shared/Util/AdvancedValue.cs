using System;
using System.Collections.Generic;

namespace Shared.Util
{
    /// <summary>
    /// Значение с множителями: база, скейлы и итоговое CurrentValue с ограничениями. Engine-agnostic (Shared):
    /// только System.* математика — счёт совпадает на сервере и клиенте (детерминизм скорости).
    /// Формула: (sumValueChanges + BaseValue·ScaleBaseValue)·ScaleCurrentValue·∏ScaleSequentially, кламп [Min,Max].
    /// </summary>
    [Serializable]
    public class AdvancedValue
    {
        public float BaseValue;

        public float ScaleBaseValue = 1;

        public float ScaleCurrentValue = 1;

        public List<float> ScaleSequentiallyValue = new List<float> { 1f };

        /// <summary>Итоговое значение после всех преобразований.</summary>
        public float CurrentValue { get; protected set; }

        public float MinValue = 0.1f;

        public float MaxValue = float.PositiveInfinity;

        protected float sumValueChanges;

        /// <summary>Вызывается при любом изменении значения (опционально, может быть null).</summary>
        public Action<float> OnUpdateValue;

        // Если true — изменения за пределами границ сохраняются в буфер и применяются по возможности.
        protected bool canOver = true;

        public AdvancedValue(float baseValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f, bool canOver = true)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;
            this.canOver = canOver;
            UpdateValue();
        }

        public AdvancedValue(float baseValue, float maxValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f, bool canOver = true)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;
            MaxValue = maxValue;
            this.canOver = canOver;
            UpdateValue();
        }

        public static float operator +(AdvancedValue a, float b) => a.CurrentValue + b;
        public static float operator -(AdvancedValue a, float b) => a.CurrentValue - b;
        public static float operator *(AdvancedValue a, float b) => a.CurrentValue * b;
        public static float operator /(AdvancedValue a, float b) => a.CurrentValue / b;

        public static float operator +(float b, AdvancedValue a) => a.CurrentValue + b;
        public static float operator -(float b, AdvancedValue a) => a.CurrentValue - b;
        public static float operator *(float b, AdvancedValue a) => a.CurrentValue * b;
        public static float operator /(float b, AdvancedValue a) => a.CurrentValue / b;

        /// <summary>Задаёт новые параметры, сбрасывает изменения и пересчитывает значение.</summary>
        public void SetNewParameters(float baseValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;

            sumValueChanges = 0;
            UpdateValue();
        }

        /// <summary>Задаёт новые параметры с MaxValue, сбрасывает изменения и пересчитывает значение.</summary>
        public void SetNewParameters(float baseValue, float maxValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;
            MaxValue = maxValue;

            sumValueChanges = 0;
            UpdateValue();
        }

        /// <summary>Добавляет value к сумме, пересчитывает CurrentValue и возвращает разницу.</summary>
        public virtual float UpdateValue(float value)
        {
            float valueNow = CurrentValue;
            sumValueChanges += value;
            float Sum = ((sumValueChanges) + BaseValue * ScaleBaseValue) * ScaleCurrentValue * SumScales();

            if (!this.canOver && Sum > MaxValue)
            {
                sumValueChanges = (MaxValue / (ScaleCurrentValue * SumScales())) - BaseValue * ScaleBaseValue;
            }
            else
            {
                // ВНИМАНИЕ: повторное += value сохранено 1-в-1 из оригинала (подозрительный double-add — value
                // учитывается в аккумуляторе дважды). На MVP-пути НЕ вызывается (Speed не мутируется) → эффекта
                // нет; ПОФИКСИТЬ до включения баффов/статов через UpdateValue(float). См. характеризационный тест.
                sumValueChanges += value;
            }

            CurrentValue = SafeClamp(Sum, MinValue, MaxValue);

            float changeValue = CurrentValue - valueNow;

            OnUpdateValue?.Invoke(changeValue);

            return changeValue;
        }

        /// <summary>Пересчитывает CurrentValue по текущим параметрам и возвращает разницу.</summary>
        public virtual float UpdateValue()
        {
            float valueNow = CurrentValue;
            float Sum = ((sumValueChanges) + BaseValue * ScaleBaseValue) * ScaleCurrentValue * SumScales();

            if (!this.canOver && Sum > MaxValue)
            {
                sumValueChanges = (MaxValue / (ScaleCurrentValue * SumScales())) - BaseValue * ScaleBaseValue;
            }

            CurrentValue = SafeClamp(Sum, MinValue, MaxValue);

            float changeValue = CurrentValue - valueNow;

            OnUpdateValue?.Invoke(changeValue);

            return changeValue;
        }

        // System.Math.Clamp бросает ArgumentException при min>max (в отличие от прежнего Mathf.Clamp). Контракт —
        // MinValue<=MaxValue; при инвертированных границах безопасно пиним к MaxValue (деградация без краша).
        private static float SafeClamp(float value, float min, float max)
        {
            if (min > max) min = max;
            return Math.Clamp(value, min, max);
        }

        public float SumScales()
        {
            float sum = 1;
            for (int i = 0; i < ScaleSequentiallyValue.Count; i++)
            {
                sum *= ScaleSequentiallyValue[i];
            }
            return sum;
        }

        public void UpdateBaseValue(float value, bool WithMinValue = false)
        {
            BaseValue += value;
            if (WithMinValue)
            {
                UpdateMinValue(value);
                return;
            }
            UpdateValue();
        }

        public void UpdateMinValue(float value)
        {
            MinValue += value;
            UpdateValue();
        }

        public void UpdateScaleCurrentValue(float value)
        {
            ScaleCurrentValue += value;
            UpdateValue();
        }

        public void UpdateScaleBaseValue(float value)
        {
            ScaleBaseValue += value;
            UpdateValue();
        }

        public void AddScaleSum(float value)
        {
            ScaleSequentiallyValue.Add(value);
            UpdateValue();
        }

        public void RemoveScaleSum(float value)
        {
            ScaleSequentiallyValue.Remove(value);
            UpdateValue();
        }
    }
}
