using UnityEngine;
using TMPro;
using Shared.Messages.Atmos;

namespace Client.UI.Atmos
{
    public sealed class AtmosHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;

        private static readonly Color CGood = new Color(0.35f, 0.85f, 0.40f);
        private static readonly Color CWarn = new Color(0.90f, 0.80f, 0.25f);
        private static readonly Color CBad = new Color(0.90f, 0.30f, 0.25f);

        private void Awake()
        {
            if (_text != null) _text.text = "—";
        }

        public void Apply(in AtmosSync sync)
        {
            if (_text == null) return;
            _text.text = $"O<sub>2</sub> {sync.OxygenKpa:F1} кПа · {sync.PressureKpa:F0} кПа";
            _text.color = sync.OxygenKpa >= 16f ? CGood : sync.OxygenKpa >= 8f ? CWarn : CBad;
        }
    }
}
