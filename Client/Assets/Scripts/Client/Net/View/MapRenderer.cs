using System.Collections.Generic;
using UnityEngine;
using Shared.World;
using Client.Map;
using Client.Config;

namespace Client.Net.View
{
    /// <summary>
    /// Рисует GridMap префабами из TileCatalog в 2.5D (этажи по Unity-Y). Рисует три слоя:
    /// активный Z и этаж ниже — обычно, непрозрачно (низ видно сквозь дыры в полу);
    /// этаж выше — только кольцами полупрозрачности вокруг проёмов в потолке (иначе срез крыши).
    /// </summary>
    public class MapRenderer : MonoBehaviour
    {
        [Header("Каталог тайлов (id → префаб/спрайт)")]
        [SerializeField] private TileCatalog _catalog;

        [Header("Этаж/высота (2.5D: этажи стоят по Unity-Y)")]
        [Tooltip("Какой Z-этаж активный (на нём игрок).")]
        [SerializeField] private int _activeZ = 0;
        [Tooltip("Доп. сдвиг пола по Y над уровнем этажа (обычно 0).")]
        [SerializeField] private float _floorYOffset = 0f;
        [Tooltip("Доп. сдвиг стены по Y над полом — против z-fighting у плоских квадов.")]
        [SerializeField] private float _wallYOffset = 0.001f;
        [SerializeField] private int _floorSortingOrder = 0;
        [SerializeField] private int _wallSortingOrder = 10;
        [Tooltip("Лёгкое расширение тайлов пола в плоскости против шва на стыках (1.0 = выкл).")]
        [SerializeField] private float _floorSeamScale = 1.02f;

        [Header("Просвечивание этажей")]
        [Tooltip("Прозрачный материал URP/Lit (Surface=Transparent, ZWrite Off) для колец верхнего этажа.")]
        [SerializeField] private Material _floorFadeMaterial;
        [Tooltip("Показывать этаж выше кольцами вокруг проёмов в потолке.")]
        [SerializeField] private bool _drawCeilingReveal = true;
        [Tooltip("Рентген: показывать ВЕСЬ верхний этаж полупрозрачным, а не только кольца у проёмов.")]
        [SerializeField] private bool _ceilingSemiTransparent = false;
        [Tooltip("Непрозрачность верхнего этажа в режиме рентгена (0..1).")]
        [Range(0f, 1f)] [SerializeField] private float _ceilingXrayAlpha = 0.4f;
        [Tooltip("Непрозрачность ВЕРХНЕГО пола по кольцам от проёма в потолке (Chebyshev: центр, 3x3, 5x5, 7x7). " +
                 "Центр прозрачный → к краю плотнее. Кольцо видимости вокруг дыры.")]
        [SerializeField] private float[] _fadeRingOpacity = { 0f, 0.18f, 0.34f, 0.50f };

        [Header("Тестовая карта (для отладки без сервера)")]
        [Tooltip("Относительный путь к .smap для загрузки при старте. Пусто = не грузить.")]
        [SerializeField] private string _testMapPath = "";

        private GridMap _map;

        // Корень-объект на каждый чанк; ключ тот же, что в GridMap (включает z).
        private readonly Dictionary<long, GameObject> _chunkRoots = new();

        // Непрозрачность тайла ВЕРХНЕГО этажа по координате (только эти тайлы Z+1 и рисуем).
        // Пересчитывается в RecomputeReveal. Низ/активный слой рисуются непрозрачно, без маски.
        private readonly Dictionary<(int, int), float> _ceilingAlpha = new();

        private MaterialPropertyBlock _mpb;
        private bool _warnedNoFadeMat;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private void Start()
        {
            if (!string.IsNullOrEmpty(_testMapPath))
                LoadLocal(_testMapPath);
        }

        // Живая подстройка просвечивания в Play Mode: меняешь тумблеры/кольца — перерисовываем.
        private void OnValidate()
        {
            if (Application.isPlaying && _map != null) SetMap(_map);
        }

        /// <summary>Загрузить карту из .smap (локальный файл). Тот же формат, что у сервера.</summary>
        public void LoadLocal(string path)
        {
            try
            {
                var map = MapSerializer.LoadFromFile(path);
                SetMap(map);
                Debug.Log($"[MapRenderer] Loaded map from {path}: {map.Chunks.Count} chunks");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapRenderer] Failed to load {path}: {ex.Message}");
            }
        }

        /// <summary>Полностью заменить карту и перерисовать активный этаж и соседние слои.</summary>
        public void SetMap(GridMap map)
        {
            ClearAll();
            _map = map;
            if (_map == null) return;

            RecomputeReveal();
            foreach (var chunk in _map.Chunks)
                if (chunk.Z >= _activeZ - 1 && chunk.Z <= _activeZ + 1)
                    ApplyChunk(chunk);
        }

        /// <summary>Сменить активный этаж и перерисовать (переходы по лестнице/лифту).</summary>
        public void SetActiveZ(int z)
        {
            if (_activeZ == z) return;
            _activeZ = z;
            if (_map != null) SetMap(_map);
        }

        /// <summary>Перерисовать чанк тайла после рантайм-изменения (TileUpdate: дверь). Дверь не меняет
        /// просвечивание, поэтому маску не пересчитываем; правки структуры пола идут через SetMap.</summary>
        public void RefreshTileAt(int x, int y, int z)
        {
            if (_map == null || z < _activeZ - 1 || z > _activeZ + 1) return;
            var chunk = _map.GetChunk(FloorDiv(x, Chunk.Size), FloorDiv(y, Chunk.Size), z);
            if (chunk != null) ApplyChunk(chunk);
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>Поворот двери по ориентации стены: стена восток-запад → поворот на 90°.</summary>
        private Quaternion DoorRotation(int x, int y, int z)
        {
            if (_map == null) return Quaternion.identity;
            bool wallEW = IsWall(x - 1, y, z) || IsWall(x + 1, y, z);
            bool wallNS = IsWall(x, y - 1, z) || IsWall(x, y + 1, z);
            if (!wallNS && wallEW) return Quaternion.Euler(0f, 90f, 0f);
            return Quaternion.identity;
        }

        // Глухой настенный объект (стена/окно) — для ориентации двери в проёме.
        private bool IsWall(int x, int y, int z)
        {
            var t = _map.GetTile(x, y, z);
            return t.StructureType != 0 && !t.Openable;
        }

        /// <summary>Посчитать маску верхнего этажа: кольца полупрозрачности вокруг проёмов в потолке
        /// (+ весь верхний пол в режиме рентгена). Низ рисуется без маски.</summary>
        private void RecomputeReveal()
        {
            _ceilingAlpha.Clear();
            if (_map == null) return;

            int radius = Mathf.Max(0, _fadeRingOpacity.Length - 1);

            foreach (var chunk in _map.Chunks)
            {
                if (chunk.Z != _activeZ + 1) continue;
                int baseX = chunk.ChunkX * Chunk.Size, baseY = chunk.ChunkY * Chunk.Size;
                for (int ly = 0; ly < Chunk.Size; ly++)
                    for (int lx = 0; lx < Chunk.Size; lx++)
                    {
                        int wx = baseX + lx, wy = baseY + ly;
                        var up = _map.GetTile(wx, wy, _activeZ + 1);

                        // Рентген: весь верхний пол виден полупрозрачным.
                        if (_ceilingSemiTransparent && up.FloorType != 0)
                            SetCeiling(wx, wy, _ceilingXrayAlpha);

                        // Проём в потолке (дыра/решётка/стекло) над полом игрока — рисуем кольцо
                        // верхнего пола с затуханием к дыре. Сплошной потолок проёма не даёт;
                        // космос снаружи станции (под проёмом нет пола Z) дырой не считается.
                        if (up.BlocksVerticalSight) continue;
                        if (_map.GetTile(wx, wy, _activeZ).FloorType == 0) continue;
                        for (int dy = -radius; dy <= radius; dy++)
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                                float op = cheb < _fadeRingOpacity.Length ? _fadeRingOpacity[cheb] : 1f;
                                SetCeiling(wx + dx, wy + dy, op);
                            }
                    }
            }

            Debug.Log($"[MapRenderer] reveal z{_activeZ}: ceilingTiles={_ceilingAlpha.Count}");
        }

        // Самый прозрачный (минимальный alpha) выигрывает на перекрытии колец.
        private void SetCeiling(int wx, int wy, float op)
        {
            var k = (wx, wy);
            float prev = _ceilingAlpha.TryGetValue(k, out var p) ? p : float.MaxValue;
            if (op < prev) _ceilingAlpha[k] = op;
        }

        /// <summary>Пересобрать инстансы тайлов одного чанка (слой по chunk.Z относительно активного).</summary>
        public void ApplyChunk(Chunk chunk)
        {
            if (_catalog == null)
            {
                Debug.LogError("[MapRenderer] TileCatalog не назначен — нечем рисовать тайлы.");
                return;
            }

            int delta = chunk.Z - _activeZ;
            if (delta < -1 || delta > 1) return;

            long key = Key(chunk.ChunkX, chunk.ChunkY, chunk.Z);
            if (_chunkRoots.TryGetValue(key, out var old) && old != null)
                Destroy(old);

            var root = new GameObject($"Chunk_{chunk.ChunkX}_{chunk.ChunkY}_z{chunk.Z}");
            root.transform.SetParent(transform, false);
            _chunkRoots[key] = root;

            int baseX = chunk.ChunkX * Chunk.Size;
            int baseY = chunk.ChunkY * Chunk.Size;
            float floorBaseY = chunk.Z * RenderConfig.FloorHeight;

            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    Tile t = chunk[lx, ly];
                    int wx = baseX + lx, wy = baseY + ly;

                    // Активный этаж и низ — непрозрачно. Верх — только тайлы из маски (кольца/рентген),
                    // остальной потолок не рисуем (срез — видно свой этаж).
                    float alpha;
                    if (delta <= 0)
                    {
                        alpha = 1f;
                    }
                    else
                    {
                        if (!_drawCeilingReveal || !_ceilingAlpha.TryGetValue((wx, wy), out alpha)) continue;
                        if (alpha <= 0.001f) continue;
                    }

                    if (t.FloorType != 0)
                    {
                        var f = _catalog.GetFloor(t.FloorType);
                        float ps = alpha < 1f ? 1f : _floorSeamScale;
                        Spawn(f?.Prefab, f?.Sprite, wx, wy, floorBaseY + _floorYOffset, _floorSortingOrder,
                            root.transform, "Floor", alignTop: true, planarScale: ps, rotation: Quaternion.identity, alpha: alpha);
                    }

                    if (t.StructureType != 0)
                    {
                        var s = _catalog.GetStructure(t.StructureType);
                        bool openNow = s != null && s.Openable && t.Open;
                        var prefab = openNow ? s.OpenPrefab : s?.Prefab;
                        var sprite = openNow ? s.OpenSprite : s?.Sprite;
                        // Открываемые (двери/люки) поворачиваем по линии стены; глухие — как есть.
                        var rot = (s != null && s.Openable) ? DoorRotation(wx, wy, chunk.Z) : Quaternion.identity;
                        Spawn(prefab, sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder,
                            root.transform, "Structure", alignTop: false, planarScale: 1f, rotation: rot, alpha: alpha);
                    }
                }
            }
        }

        /// <summary>Создать инстанс тайла (префаб или fallback-спрайт) в центре тайла, выровнять по грани,
        /// при alpha&lt;1 — погасить через прозрачный материал.</summary>
        private void Spawn(GameObject prefab, Sprite sprite, int wx, int wy, float y, int sortingOrder,
            Transform parent, string label, bool alignTop, float planarScale, Quaternion rotation, float alpha = 1f)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, parent);
            }
            else if (sprite != null)
            {
                go = new GameObject(label);
                go.transform.SetParent(parent, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = sortingOrder;
                var col = sr.color; col.a = alpha; sr.color = col;
            }
            else
            {
                return; // ни префаба, ни спрайта
            }

            go.transform.rotation = rotation;

            if (planarScale != 1f)
            {
                Vector3 s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x * planarScale, s.y, s.z * planarScale);
            }

            // +0.5 — центр тайла (целое (wx,wy) — угол), чтобы сетка совпала с коллизией.
            go.transform.position = new Vector3(wx + 0.5f, y, wy + 0.5f);

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            AlignEdgeTo(go, renderers, y, alignTop);
            if (alpha < 1f && prefab != null) ApplyFade(renderers, alpha);
        }

        // Погасить меш-рендеры объекта: подменить материал на прозрачный, перенести текстуру/цвет
        // оригинала с заданной alpha через MaterialPropertyBlock (без аллокаций материалов).
        private void ApplyFade(Renderer[] renderers, float alpha)
        {
            if (_floorFadeMaterial == null)
            {
                if (!_warnedNoFadeMat)
                {
                    Debug.LogWarning("[MapRenderer] _floorFadeMaterial не назначен — полупрозрачность верхнего этажа не работает.");
                    _warnedNoFadeMat = true;
                }
                return;
            }

            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue; // спрайтам alpha уже задана через color

                var orig = r.sharedMaterial;
                _mpb.Clear(); // иначе _BaseMap протекает с предыдущего рендера (SetTexture условный)

                Color c = (orig != null && orig.HasProperty(BaseColorId)) ? orig.GetColor(BaseColorId) : Color.white;
                c.a = alpha;
                _mpb.SetColor(BaseColorId, c);

                if (orig != null && orig.HasProperty(BaseMapId))
                {
                    var tex = orig.GetTexture(BaseMapId);
                    if (tex != null) _mpb.SetTexture(BaseMapId, tex);
                }

                // Подменяем материал на всех сабмешах (не только слот 0), иначе мультиматериальный
                // префаб останется частично непрозрачным.
                int count = r.sharedMaterials.Length;
                if (count <= 1)
                {
                    r.sharedMaterial = _floorFadeMaterial;
                }
                else
                {
                    var mats = new Material[count];
                    for (int i = 0; i < count; i++) mats[i] = _floorFadeMaterial;
                    r.sharedMaterials = mats;
                }

                r.SetPropertyBlock(_mpb);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // прозрачный потолок не льёт тень
            }
        }

        /// <summary>Сдвинуть объект по Y, чтобы его верхняя/нижняя грань легла на targetY.</summary>
        private static void AlignEdgeTo(GameObject go, Renderer[] renderers, float targetY, bool alignTop)
        {
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            float edge = alignTop ? b.max.y : b.min.y;
            go.transform.position += new Vector3(0f, targetY - edge, 0f);
        }

        private void ClearAll()
        {
            foreach (var root in _chunkRoots.Values)
                if (root != null) Destroy(root);
            _chunkRoots.Clear();
        }

        // Та же упаковка ключа, что в GridMap.
        private static long Key(int cx, int cy, int z)
        {
            return ((long)(cx & 0x1FFFFF))
                 | ((long)(cy & 0x1FFFFF) << 21)
                 | ((long)(z & 0x1FFFFF) << 42);
        }
    }
}
