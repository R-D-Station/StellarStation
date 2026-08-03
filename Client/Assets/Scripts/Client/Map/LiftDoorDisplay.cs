using UnityEngine;
using TMPro;
using Client.Lifts;
using Shared.Messages.Lifts;
using Shared.World.Blocks;

namespace Client.Map
{
    public sealed class LiftDoorDisplay : MonoBehaviour, IBlockComponent, ILiftDoorDisplay
    {
        [Header("Панели")]
        [Tooltip("Номер этажа, на котором сейчас кабина.")]
        [SerializeField] private TMP_Text _floorPanel;
        [Tooltip("Индикатор «едет вверх» — гаснет, когда кабина стоит.")]
        [SerializeField] private TMP_Text _upPanel;
        [Tooltip("Индикатор «едет вниз» — гаснет, когда кабина стоит.")]
        [SerializeField] private TMP_Text _downPanel;

        [Header("Кнопка")]
        [Tooltip("Renderer кнопки вызова; цвет меняется через MaterialPropertyBlock.")]
        [SerializeField] private Renderer _callButton;
        [Tooltip("Свойство цвета в шейдере кнопки.")]
        [SerializeField] private string _colorProperty = "_BaseColor";
        [SerializeField] private Color _buttonIdle = Color.white;
        [SerializeField] private Color _buttonCalled = new Color(1f, 0.55f, 0.1f, 1f);

        private const float RenderEps = 0.001f;

        private static MaterialPropertyBlock _mpb;

        private long _cellKey;
        private bool _registered;
        private float _alpha = 1f;
        private LiftDoorDisplayState _state;
        private int _colorId;
        private bool _colorIdReady;
        private Color _appliedColor;
        private bool _colorApplied;

        public bool IsAlive => this != null;

        public void OnSpawn(in BlockContext ctx)
        {
            if (_registered)
                LiftDoorDisplayHub.Registry.Unregister(_cellKey, this);
            _cellKey = BlockGrid.PackCell(ctx.X, ctx.Y, ctx.Z);
            _state = default;
            _alpha = 1f;
            _colorApplied = false;
            Write();
            LiftDoorDisplayHub.Registry.Register(_cellKey, this);
            _registered = true;
        }

        public void OnDespawn()
        {
            if (_registered)
                LiftDoorDisplayHub.Registry.Unregister(_cellKey, this);
            _registered = false;
            _state = default;
            _alpha = 1f;
            Write();
        }

        public void OnVisibility(float alpha)
        {
            _alpha = alpha;
            Write();
        }

        public void Apply(in LiftDoorDisplayState state)
        {
            _state = state;
            Write();
        }

        private void Write()
        {
            bool known = _alpha > RenderEps && _state.Known;
            WritePanel(_floorPanel, known, known ? LiftDisplayText.Floor(_state.CabinLabelIndex) : string.Empty);
            WritePanel(_upPanel, known && _state.Direction > 0, null);
            WritePanel(_downPanel, known && _state.Direction < 0, null);
            WriteButton(_state.Called ? _buttonCalled : _buttonIdle);
        }

        private void WritePanel(TMP_Text panel, bool on, string text)
        {
            if (panel == null)
                return;
            if (text != null && !string.Equals(panel.text, text))
                panel.text = text;
            panel.alpha = _alpha;
            if (panel.enabled != on)
                panel.enabled = on;
        }

        private void WriteButton(Color color)
        {
            if (_callButton == null || (_colorApplied && _appliedColor == color))
                return;
            if (!_colorIdReady)
            {
                _colorId = Shader.PropertyToID(string.IsNullOrEmpty(_colorProperty) ? "_BaseColor" : _colorProperty);
                _colorIdReady = true;
            }
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _callButton.GetPropertyBlock(_mpb);
            _mpb.SetColor(_colorId, color);
            _callButton.SetPropertyBlock(_mpb);
            _appliedColor = color;
            _colorApplied = true;
        }
    }
}
