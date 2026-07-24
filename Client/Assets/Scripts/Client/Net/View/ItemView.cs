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
        [Tooltip("Скорость догона визуала к авторитетной позиции (блоков/сек); телепорт >2 блоков — мгновенный снап.")]
        [SerializeField] private float _smoothSpeed = 6f;

        public int NetId { get; private set; }

        // Хук поверхности ячейки (x,высота,план→Y мира); ставится NetworkRunner при входе в блок-режим.
        public static System.Func<int, int, int, float> BlockSurface;

        private ushort _lastDefId;   // перевыбор спрайта только при смене ItemDefId
        private byte _lastOpen;
        private bool _hasDef;
        private Vector3 _targetPos;
        private bool _hasTarget;
        private RenderLayerCatalog _renderLayers;
        private int _spawnSeq;
        private int _layerOrder;
        private Material _baseMaterial;
        private Material _outlineMaterial;

        private static readonly System.Collections.Generic.HashSet<string> _unreadableWarned = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.HashSet<int> _orderClampWarned = new System.Collections.Generic.HashSet<int>();

        public ushort ItemDefId => _lastDefId;

        public int SortingOrder => _spriteRenderer != null ? _spriteRenderer.sortingOrder : 0;

        public void SetRenderLayers(RenderLayerCatalog c) => _renderLayers = c;

        public void Init(int netId)
        {
            NetId = netId;
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null) _baseMaterial = _spriteRenderer.sharedMaterial;
        }

        public void SetSpawnSeq(int s)
        {
            _spawnSeq = s;
            if (_hasDef && _spriteRenderer != null && _renderLayers != null)
                _spriteRenderer.sortingOrder = _layerOrder * 1000 + _spawnSeq;
        }

        public void SetOutlineMaterial(Material m) => _outlineMaterial = m;

        public void SetHovered(bool on)
        {
            if (_spriteRenderer == null || _outlineMaterial == null || _baseMaterial == null) return;
            _spriteRenderer.sharedMaterial = on ? _outlineMaterial : _baseMaterial;
        }

        public bool HitTestPixel(Ray ray, out float camDist)
        {
            camDist = 0f;
            if (_spriteRenderer == null) return false;
            var sprite = _spriteRenderer.sprite;
            if (sprite == null) return false;
            if (!_spriteRenderer.bounds.IntersectRay(ray)) return false;

            var srt = _spriteRenderer.transform;
            if (Mathf.Abs(Vector3.Dot(srt.forward, ray.direction)) < 0.02f) return true;

            var plane = new Plane(srt.forward, srt.position);
            if (!plane.Raycast(ray, out float enter)) return false;
            camDist = enter;

            Vector3 local = srt.InverseTransformPoint(ray.GetPoint(enter));
            Bounds sb = sprite.bounds;
            if (sb.size.x <= 0f || sb.size.y <= 0f) return false;
            float u = (local.x - sb.min.x) / sb.size.x;
            float v = (local.y - sb.min.y) / sb.size.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

            var tex = sprite.texture;
            if (!tex.isReadable)
            {
                if (_unreadableWarned.Add(tex.name))
                    Debug.LogWarning($"[ItemView] Текстура '{tex.name}' без Read/Write — пиксельный клик-тест деградирует до bounds.");
                return true;
            }

            Rect tr = sprite.textureRect;
            int px = Mathf.Clamp((int)(tr.x + u * tr.width), (int)tr.x, (int)(tr.x + tr.width) - 1);
            int py = Mathf.Clamp((int)(tr.y + v * tr.height), (int)tr.y, (int)(tr.y + tr.height) - 1);
            try
            {
                return tex.GetPixel(px, py).a > 0.1f;
            }
            catch (UnityException)
            {
                if (_unreadableWarned.Add(tex.name))
                    Debug.LogWarning($"[ItemView] Текстура '{tex.name}' недоступна для чтения (tight-pack атлас?) — пиксельный клик-тест деградирует до bounds.");
                return true;
            }
        }

        /// <summary>Применить данные предмета из снапшота: позиция (центр ячейки) + спрайт по ItemDefId.</summary>
        public void Apply(in ItemInstance data, ItemCatalog catalog)
        {
            if (NetEntityView.BlocksMode)
            {
                // Блок-режим: оси 1:1 (X/Y=высота/Z=план), без тайловой Z·FloorHeight; предмет лежит на верхе бокса ячейки.
                float surfaceY = BlockSurface != null
                    ? BlockSurface(Mathf.FloorToInt(data.X), Mathf.FloorToInt(data.Z), Mathf.FloorToInt(data.Y))
                    : data.Z;
                _targetPos = new Vector3(data.X, surfaceY, data.Y);
            }
            else
            {
                // Сервер (X, Y=глубина, Z=этаж) → Unity (X, высота=Z·FloorHeight, Z=глубина). +0.5 — центр ячейки.
                _targetPos = new Vector3(data.X, data.Z * RenderConfig.FloorHeight, data.Y);
            }

            if (!_hasTarget)
            {
                _hasTarget = true;
                transform.position = _targetPos;
            }

            if (!_hasDef || data.ItemDefId != _lastDefId || data.Open != _lastOpen)
            {
                _lastDefId = data.ItemDefId;
                _lastOpen = data.Open;
                _hasDef = true;
                if (_spriteRenderer != null && catalog != null)
                {
                    var def = catalog.For(data.ItemDefId);
                    var spr = def != null ? (data.Open == 1 && def.OpenSprite != null ? def.OpenSprite : def.Sprite) : null;
                    _spriteRenderer.sprite = spr;

                    if (def != null)
                    {
                        // Спрайт рендерится в RenderScale·TileSize НЕЗАВИСИМО от импорта — нормируем по натуральному мир-размеру спрайта.
                        float target = def.RenderScale * RenderConfig.TileSize;
                        float native = spr != null ? Mathf.Max(spr.bounds.size.x, spr.bounds.size.y) : 1f;
                        transform.localScale = native > 0.0001f ? Vector3.one * (target / native) : Vector3.one;

                        if (_renderLayers != null)
                        {
                            int rawOrder = _renderLayers.OrderOf(def.RenderLayer);
                            _layerOrder = Mathf.Clamp(rawOrder, -32, 31);
                            if (rawOrder != _layerOrder && _orderClampWarned.Add(rawOrder))
                                Debug.LogWarning($"[ItemView] RenderLayer Order={rawOrder} вне диапазона [-32..31] (sortingOrder — 16-бит) — зажат до {_layerOrder}; поправьте Order в RenderLayerCatalog.");
                            _spriteRenderer.sortingOrder = _layerOrder * 1000 + _spawnSeq;
                        }
                    }
                }
            }
        }

        private void Update()
        {
            if (!_hasTarget) return;
            Vector3 pos = transform.position;
            if (pos == _targetPos) return;
            if ((_targetPos - pos).sqrMagnitude > 4f)
                transform.position = _targetPos;
            else
                transform.position = Vector3.MoveTowards(pos, _targetPos, _smoothSpeed * Time.deltaTime);
        }
    }
}
