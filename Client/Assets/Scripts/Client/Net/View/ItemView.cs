using UnityEngine;
using Shared.World.Items;
using Client.Config;
using Client.Items;

namespace Client.Net.View
{
    /// <summary>Визуал наземного предмета: статичный world-спрайт в центре дискретной ячейки. Без SnapshotBuffer —
    /// предмет не движется (Sequenced-снапшот задаёт позицию сразу).</summary>
    public class ItemView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;

        public int NetId { get; private set; }

        private ushort _lastDefId;   // перевыбор спрайта только при смене ItemDefId
        private bool _hasDef;

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>Применить данные предмета из снапшота: позиция (центр ячейки) + спрайт по ItemDefId.</summary>
        // ЕДИНСТВЕННОЕ место маппинга дискретной ячейки в визуальную высоту (выбрасываемый рендер; блок-мир его заменит).
        public void Apply(in ItemInstance data, ItemCatalog catalog)
        {
            // Сервер (X, Y=глубина, Z=этаж) → Unity (X, высота=Z·FloorHeight, Z=глубина). +0.5 — центр ячейки.
            transform.position = new Vector3(data.X + 0.5f, data.Z * RenderConfig.FloorHeight, data.Y + 0.5f);

            if (!_hasDef || data.ItemDefId != _lastDefId)
            {
                _lastDefId = data.ItemDefId;
                _hasDef = true;
                if (_spriteRenderer != null && catalog != null)
                {
                    var def = catalog.For(data.ItemDefId);
                    _spriteRenderer.sprite = def != null ? def.Sprite : null;

                    if (def != null)
                    {
                        // Спрайт рендерится в RenderScale·TileSize НЕЗАВИСИМО от импорта — нормируем по натуральному мир-размеру спрайта.
                        float target = def.RenderScale * RenderConfig.TileSize;
                        var spr = _spriteRenderer.sprite; // уже назначен выше
                        float native = spr != null ? Mathf.Max(spr.bounds.size.x, spr.bounds.size.y) : 1f;
                        transform.localScale = native > 0.0001f ? Vector3.one * (target / native) : Vector3.one;
                    }
                }
            }
        }
    }
}
