using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace ValuesEye
{
    /// <summary>
    /// Класс для удобной работы с множителями к различным параметрам
    /// </summary>
    [System.Serializable]
    public class AdvansedValue
    {
        /// <summary>
        /// Все вычисления происходят над эти значение, оно остаётся не изменным
        /// </summary>
        public float BaseValue;


        /// <summary>
        /// Множитель базового значение (<typeparam name="BaseValue">)
        /// </summary>
        public float ScaleBaseValue = 1;


        /// <summary>
        /// Множитель текущиего значения (<typeparam name="CurrentValue")
        /// </summary>
        public float ScaleCurrentValue = 1;


        /// <summary>
        /// Множители базового значения, которые перемножаются между собой
        /// </summary>
        public List<float> ScaleSequentiallyValue = new List<float> { 1f };


        /// <summary>
        /// Показывает значение после всех трансформаций над ним
        /// </summary>
        public float CurrentValue { get; protected set; } // Текущие значение 


        /// <summary>
        /// Ограничение по минимому
        /// </summary>
        public float MinValue = 0.1f; // Минимальное значение


        /// <summary>
        /// Ограничение по максимуму
        /// </summary>
        public float MaxValue = Mathf.Infinity;


        /// <summary>
        /// Все изменения над базовым значением
        /// </summary>
        [SerializeField] protected float _SumValueChanges; // Все изменения над значением


        /// <summary>
        /// События при любом изменении значения
        /// </summary>
        public UnityAction<float> OnUpdateValue; // Собитые при изменении характеристики


        /// <summary>
        /// Если true - все изменения выходящие за границы значений, будут сохранены в буфер и применены по возможности
        /// </summary>
        protected bool CanOver = true;


        /// <summary>
        /// Инициализация
        /// </summary>
        /// <param name="baseValue"></param>
        /// <param name="scaleBaseValue"></param>
        /// <param name="scaleCurrentValue"></param>
        /// <param name="minValue"></param>
        /// <param name="canOver"></param>
        public AdvansedValue(float baseValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f, bool canOver = true)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;
            CanOver = canOver;
            UpdateValue();
        }


        /// <summary>
        /// Инициализация
        /// </summary>
        /// <param name="baseValue"></param>
        /// <param name="scaleBaseValue"></param>
        /// <param name="scaleCurrentValue"></param>
        /// <param name="minValue"></param>
        /// /// <param name="canOver"></param>
        public AdvansedValue(float baseValue, float maxValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f, bool canOver = true)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;
            MaxValue = maxValue;
            CanOver = canOver;
            UpdateValue();
        }


        public static float operator +(AdvansedValue a, float b)
        {
            return a.CurrentValue + b;
        }
        public static float operator -(AdvansedValue a, float b)
        {
            return a.CurrentValue - b;
        }
        public static float operator *(AdvansedValue a, float b)
        {
            return a.CurrentValue * b;
        }
        public static float operator /(AdvansedValue a, float b)
        {
            return a.CurrentValue / b;
        }

        public static float operator +(float b, AdvansedValue a)
        {
            return a.CurrentValue + b;
        }
        public static float operator -(float b, AdvansedValue a)
        {
            return a.CurrentValue - b;
        }
        public static float operator *(float b, AdvansedValue a)
        {
            return a.CurrentValue * b;
        }
        public static float operator /(float b, AdvansedValue a)
        {
            return a.CurrentValue / b;
        }

        public static Vector3 operator *(Vector3 a, AdvansedValue b)
        {
            return a * b.CurrentValue;
        }

        /// <summary>
        /// Устанавливает новые значения не создавая новый класс, затем отчищает все изменения и применяет UpdateValue
        /// </summary>
        /// <param name="baseValue"></param>
        /// <param name="scaleBaseValue"></param>
        /// <param name="scaleCurrentValue"></param>
        /// <param name="minValue"></param>
        public void SetNewParameters(float baseValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;

            _SumValueChanges = 0;
            UpdateValue();
        }


        /// <summary>
        /// Устанавливает новые значения не создавая новый класс, затем отчищает все изменения и применяет UpdateValue
        /// </summary>
        /// <param name="baseValue"></param>
        /// <param name="MaxValue"></param>
        /// <param name="scaleBaseValue"></param>
        /// <param name="scaleCurrentValue"></param>
        /// <param name="minValue"></param>
        public void SetNewParameters(float baseValue, float maxValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f)
        {
            BaseValue = baseValue;
            ScaleBaseValue = scaleBaseValue;
            ScaleCurrentValue = scaleCurrentValue;
            MinValue = minValue;
            MaxValue = maxValue;

            _SumValueChanges = 0;
            UpdateValue();
        }


        /// <summary>
        /// Выполняет все преобразования над значением BaseValue и возвращает разницу между старым и новым значением
        /// </summary>
        /// <param name="value">Adds value to all sum</param>
        /// <returns>Difference in change</returns>
        public virtual float UpdateValue(float value)
        {
            float valueNow = CurrentValue;
            _SumValueChanges += value;
            float Sum = ((_SumValueChanges) + BaseValue * ScaleBaseValue) * ScaleCurrentValue * SumScales();

            if (!CanOver && Sum > MaxValue)
            {
                _SumValueChanges = (MaxValue / (ScaleCurrentValue * SumScales())) - BaseValue * ScaleBaseValue;
            }
            else
            {
                _SumValueChanges += value;
            }

            CurrentValue = Mathf.Clamp(Sum, MinValue, MaxValue);

            float changeValue = CurrentValue - valueNow;

            OnUpdateValue?.Invoke(changeValue);

            return changeValue;
        }


        /// <summary>
        /// Выполняет все преобразования над значением BaseValue и возвращает разницу между старым и новым значением
        /// </summary>
        /// <returns>Difference in change</returns>
        public virtual float UpdateValue()
        {
            float valueNow = CurrentValue;
            float Sum = ((_SumValueChanges) + BaseValue * ScaleBaseValue) * ScaleCurrentValue * SumScales();

            if (!CanOver && Sum > MaxValue)
            {
                _SumValueChanges = (MaxValue / (ScaleCurrentValue * SumScales())) - BaseValue * ScaleBaseValue;
            }

            CurrentValue = Mathf.Clamp(Sum, MinValue, MaxValue);

            float changeValue = CurrentValue - valueNow;

            OnUpdateValue?.Invoke(changeValue);

            return changeValue;
        }


        /// <summary>
        /// Суммирует множители базового значения
        /// </summary>
        /// <returns>Value of sum</returns>
        public float SumScales()
        {
            float sum = 1;
            for (int i = 0; i < ScaleSequentiallyValue.Count; i++)
            {
                sum *= ScaleSequentiallyValue[i];
            }
            return sum;
        }


        /// <summary>
        /// Обновляет BaseValue, затем приминяет UpdateValue
        /// </summary>
        /// <param name="value"></param>
        /// <param name="WithMinValue">if true adds to min value too</param>
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


        /// <summary>
        /// Обновляет ограничение по минимому, затем приминяет UpdateValue
        /// </summary>
        /// <param name="value"></param>
        public void UpdateMinValue(float value)
        {
            MinValue += value;
            UpdateValue();
        }


        /// <summary>
        /// Обновляет ScaleCurrentValue, затем приминяет UpdateValue
        /// </summary>
        /// <param name="value"></param>
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

    [CustomEditor(typeof(AdvansedValue))]
    public class AdvansedValueEditor : Editor
    {
        protected SerializedProperty m_BaseValue;

        private void OnEnable()
        {
            m_BaseValue = serializedObject.FindProperty("BaseValue");
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.PropertyField(m_BaseValue);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
