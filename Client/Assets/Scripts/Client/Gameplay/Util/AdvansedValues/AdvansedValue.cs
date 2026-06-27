using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace Client.Gameplay.Util.AdvancedValues;

/// <summary>
/// Значение с множителями: база, скейлы и итоговое CurrentValue с ограничениями.
/// </summary>
[System.Serializable]
public class AdvancedValue
{
    public float BaseValue;

    public float ScaleBaseValue = 1;

    public float ScaleCurrentValue = 1;

    public List<float> ScaleSequentiallyValue = new List<float> { 1f };

    /// <summary>
    /// Итоговое значение после всех преобразований.
    /// </summary>
    public float CurrentValue { get; protected set; }

    public float MinValue = 0.1f;

    public float MaxValue = Mathf.Infinity;

    [SerializeField]
    protected float sumValueChanges;

    /// <summary>
    /// Вызывается при любом изменении значения.
    /// </summary>
    public UnityAction<float> OnUpdateValue;

    // Если true — изменения за пределами границ сохраняются в буфер и применяются по возможности
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


    public static float operator +(AdvancedValue a, float b)
    {
        return a.CurrentValue + b;
    }
    public static float operator -(AdvancedValue a, float b)
    {
        return a.CurrentValue - b;
    }
    public static float operator *(AdvancedValue a, float b)
    {
        return a.CurrentValue * b;
    }
    public static float operator /(AdvancedValue a, float b)
    {
        return a.CurrentValue / b;
    }

    public static float operator +(float b, AdvancedValue a)
    {
        return a.CurrentValue + b;
    }
    public static float operator -(float b, AdvancedValue a)
    {
        return a.CurrentValue - b;
    }
    public static float operator *(float b, AdvancedValue a)
    {
        return a.CurrentValue * b;
    }
    public static float operator /(float b, AdvancedValue a)
    {
        return a.CurrentValue / b;
    }

    public static Vector3 operator *(Vector3 a, AdvancedValue b)
    {
        return a * b.CurrentValue;
    }

    /// <summary>
    /// Задаёт новые параметры, сбрасывает изменения и пересчитывает значение.
    /// </summary>
    public void SetNewParameters(float baseValue, float scaleBaseValue = 1, float scaleCurrentValue = 1, float minValue = 0.1f)
    {
        BaseValue = baseValue;
        ScaleBaseValue = scaleBaseValue;
        ScaleCurrentValue = scaleCurrentValue;
        MinValue = minValue;

        sumValueChanges = 0;
        UpdateValue();
    }


    /// <summary>
    /// Задаёт новые параметры с MaxValue, сбрасывает изменения и пересчитывает значение.
    /// </summary>
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


    /// <summary>
    /// Добавляет value к сумме, пересчитывает CurrentValue и возвращает разницу.
    /// </summary>
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
            sumValueChanges += value;
        }

        CurrentValue = Mathf.Clamp(Sum, MinValue, MaxValue);

        float changeValue = CurrentValue - valueNow;

        OnUpdateValue?.Invoke(changeValue);

        return changeValue;
    }


    /// <summary>
    /// Пересчитывает CurrentValue по текущим параметрам и возвращает разницу.
    /// </summary>
    public virtual float UpdateValue()
    {
        float valueNow = CurrentValue;
        float Sum = ((sumValueChanges) + BaseValue * ScaleBaseValue) * ScaleCurrentValue * SumScales();

        if (!this.canOver && Sum > MaxValue)
        {
            sumValueChanges = (MaxValue / (ScaleCurrentValue * SumScales())) - BaseValue * ScaleBaseValue;
        }

        CurrentValue = Mathf.Clamp(Sum, MinValue, MaxValue);

        float changeValue = CurrentValue - valueNow;

        OnUpdateValue?.Invoke(changeValue);

        return changeValue;
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
#if UNITY_EDITOR
/// <summary>
/// Кастомный инспектор для AdvancedValue.
/// </summary>
[CustomEditor(typeof(AdvancedValue))]
public class AdvancedValueEditor : Editor
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
#endif

