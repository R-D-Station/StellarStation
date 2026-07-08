using UnityEngine;
using UnityEngine.InputSystem;
using Client.Net;
using Client.Net.View;

namespace Client.Gameplay.Diagnostics
{
    /// <summary>ДИАГНОСТИКА углового слоя пола: показывает форму/маску/`_cur_corner`/`_rotate` тайла (под курсором или заданного
    /// X/Y/Z) поверх экрана. Повесь на любой GameObject в игровой сессии, назначь MapRenderer (и опц. NetworkRunner для пика
    /// под курсором). Временный инструмент — снять/удалить после отладки.</summary>
    public sealed class CornerProbe : MonoBehaviour
    {
        [Tooltip("Рендерер карты — источник данных (ОБЯЗАТЕЛЬНО).")]
        [SerializeField] private MapRenderer _mapRenderer;
        [Tooltip("Опц.: раннер. Если задан и включён PickUnderCursor — тайл берётся ПОД КУРСОРОМ (наведи на проблемный).")]
        [SerializeField] private NetworkRunner _runner;

        [Header("Выбор тайла")]
        [Tooltip("Брать тайл под курсором (нужен NetworkRunner). Выкл → ручные X/Y/Z ниже.")]
        [SerializeField] private bool _pickUnderCursor = true;
        [SerializeField] private int _x;
        [SerializeField] private int _y;
        [SerializeField] private int _z;

        [Header("Оверлей")]
        [SerializeField] private int _fontSize = 16;

        private string _text = "CornerProbe: назначь MapRenderer";
        private GUIStyle _style;

        private void Update()
        {
            if (_mapRenderer == null) { _text = "CornerProbe: назначь MapRenderer"; return; }

            int x = _x, y = _y, z = _z;
            if (_pickUnderCursor && _runner != null && Mouse.current != null
                && _runner.TryResolveHoverTile(Mouse.current.position.ReadValue(), out int hx, out int hy, out int hz))
            {
                x = hx; y = hy; z = hz;
            }

            _text = _mapRenderer.DescribeFloorCorner(x, y, z);
        }

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = Color.yellow }
            };

            var rect = new Rect(8f, 8f, Mathf.Min(900f, Screen.width - 16f), 64f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, _text, _style);
        }
    }
}
