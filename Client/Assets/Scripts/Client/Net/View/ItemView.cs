using UnityEngine;
using Shared.World.Items;
using Client.Config;
using Client.Items;

namespace Client.Net.View
{
    /// <summary>Визуал наземного предмета: статичный world-спрайт в центре ячейки, без SnapshotBuffer (не движется).</summary>
    public class ItemView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;

        public int NetId { get; private set; }

        // Хук поверхности ячейки (x,высота,план→Y мира); ставится NetworkRunner при входе в блок-режим.
        public static System.Func<int, int, int, float> BlockSurface;

        private ushort _lastDefId;   // перевыбор спрайта только при смене ItemDefId
        private bool _hasDef;

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>Применить данные предмета из снапшота: позиция (центр ячейки) + спрайт по ItemDefId.</summary>
        public void Apply(in ItemInstance data, ItemCatalog catalog)
        {
            if (NetEntityView.BlocksMode)
            {
                // Блок-режим: оси 1:1 (X/Y=высота/Z=план), без тайловой Z·FloorHeight; предмет лежит на верхе бокса ячейки.
                float surfaceY = BlockSurface != null ? BlockSurface(data.X, data.Z, data.Y) : data.Z;
                transform.position = new Vector3(data.X + 0.5f, surfaceY, data.Y + 0.5f);
            }
            else
            {
                // Сервер (X, Y=глубина, Z=этаж) → Unity (X, высота=Z·FloorHeight, Z=глубина). +0.5 — центр ячейки.
                transform.position = new Vector3(data.X + 0.5f, data.Z * RenderConfig.FloorHeight, data.Y + 0.5f);
            }

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
