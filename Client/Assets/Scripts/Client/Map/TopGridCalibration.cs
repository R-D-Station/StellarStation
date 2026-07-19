using UnityEngine;
using Shared.World;

namespace Client.Map
{
    /// <summary>Доп. повороты ×90° верха-грида поверх базовых правил WallCornerCode: основной слой и слой углов, один ассет на атлас.</summary>
    [CreateAssetMenu(menuName = "Station/Top Grid Calibration", fileName = "TopGridCalibration")]
    public sealed class TopGridCalibration : ScriptableObject
    {
        [Tooltip("Доп. поворот ОСНОВНОГО слоя по форме (четверти ×90°). Крутит весь квад; компенсация для углов автоматическая.")]
        public int[] MainQuarter = new int[6];    // [(int)WallShape]

        [Tooltip("Доп. поворот СЛОЯ УГЛОВ (четверти ×90°): строка — форма, колонка — _cur_corner 0..5 (0 не используется).")]
        public int[] CornerQuarter = new int[36]; // [(int)WallShape * 6 + curCorner]

        /// <summary>Доп. четверть основного слоя для формы (0..3).</summary>
        public int Main(WallShape shape)
        {
            int i = (int)shape;
            return MainQuarter != null && i >= 0 && i < MainQuarter.Length ? MainQuarter[i] & 3 : 0;
        }

        /// <summary>Доп. четверть углового слоя для пары форма×тип угла (0..3).</summary>
        public int Corner(WallShape shape, int curCorner)
        {
            int i = (int)shape * 6 + curCorner;
            return CornerQuarter != null && i >= 0 && i < CornerQuarter.Length ? CornerQuarter[i] & 3 : 0;
        }

#if UNITY_EDITOR
        /// <summary>Калибровка изменилась (правка в инспекторе) — авторинг перерисовывает карту.</summary>
        public static event System.Action Changed;
        private void OnValidate() => Changed?.Invoke();
#endif
    }
}
