using UnityEngine;
using UnityEngine.UI;

namespace Client.UI.Util
{
    /// <summary>Вписывает Image в макс-бокс (рамку) с сохранением пропорций спрайта (letterbox/pillarbox по центру);
    /// пере-вписывает при смене спрайта или размера бокса. Переиспользуемый компонент (не только инвентарь).</summary>
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public sealed class SpriteAspectFitter : MonoBehaviour
    {
        [SerializeField] private Image _image;
        [Tooltip("Макс-бокс (рамка). Пусто = родитель. Image впишется СЮДА с сохранением пропорций спрайта.")]
        [SerializeField] private RectTransform _box;
        [Tooltip("Равномерный отступ внутрь бокса, px.")]
        [SerializeField] private float _padding = 0f;

        private RectTransform _rt;
        private RectTransform Rt => _rt != null ? _rt : (_rt = transform as RectTransform);

        private Sprite _lastSprite;
        private Vector2 _lastBoxSize;
        private bool _fitting; // защита от рекурсии: sizeDelta в Fit триггерит OnRectTransformDimensionsChange → Fit

        private void OnEnable()
        {
            if (_image == null) _image = GetComponent<Image>();
            Fit();
        }

        // Спрайт HUD ставит в ЭТОМ кадре (InventoryHud из ItemCatalog) → сверяемся и вписываем в LateUpdate, ПОСЛЕ него.
        private void LateUpdate()
        {
            RectTransform box = _box != null ? _box : Rt.parent as RectTransform;
            Vector2 rawBox = box != null ? box.rect.size : Vector2.zero;
            Sprite spr = _image != null ? _image.sprite : null;
            if (spr != _lastSprite || rawBox != _lastBoxSize)
                Fit();
        }

        private void OnRectTransformDimensionsChange() => Fit(); // страховка на layout-ресайз бокса/иконки

        private void Fit()
        {
            if (_fitting) return;

            RectTransform box = _box != null ? _box : Rt.parent as RectTransform;
            if (box == null) return;

            Vector2 rawBox = box.rect.size;
            Vector2 boxSize = new Vector2(rawBox.x - 2f * _padding, rawBox.y - 2f * _padding);
            if (boxSize.x <= 0f || boxSize.y <= 0f) return;

            Sprite spr = _image != null ? _image.sprite : null;
            if (spr == null) return;

            float sa = spr.rect.width / spr.rect.height; // sprite.rect устойчив для атласных
            float ba = boxSize.x / boxSize.y;
            Vector2 fit = (ba > sa) ? new Vector2(boxSize.y * sa, boxSize.y) : new Vector2(boxSize.x, boxSize.x / sa);

            _fitting = true;
            Rt.anchorMin = Rt.anchorMax = Rt.pivot = new Vector2(0.5f, 0.5f);
            Rt.anchoredPosition = Vector2.zero;
            Rt.sizeDelta = fit;
            _fitting = false;

            _lastSprite = spr;
            _lastBoxSize = rawBox;
        }
    }
}
