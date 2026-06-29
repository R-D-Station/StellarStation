using System.Collections.Generic;
using UnityEngine;
using Shared.World;
using Shared.Simulation;
using Client.Map;
using Client.Config;

namespace Client.Net.View
{
    /// <summary>
    /// Рисует GridMap префабами из TileCatalog в 2.5D (этажи по Unity-Y). Активный Z и z-1 — непрозрачно;
    /// этажи z±2…z±maxFloors раскрываются reveal'ом через вертикальные шахты дыр: кандидаты вокруг каждой
    /// дыры берутся shadowcast-Fov на её уровне (тень за стенами), связные дыры = кластер-проём.
    /// Видимость/непрозрачность кольца — per-frame по близости игрока к кластеру (без ребилда чанков).
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
        [Tooltip("Градиент непрозрачности кольца от проёма к краю (нормируется на текущий радиус). " +
                 "Индекс 0 = у проёма (прозрачнее), последний = край кольца (плотнее).")]
        [SerializeField] private float[] _fadeRingOpacity = { 0f, 0.18f, 0.34f, 0.50f };

        [Header("Динамический просвет потолка (R1)")]
        [Tooltip("Радиус кольца (тайлы), когда игрок далеко от проёма.")]
        [SerializeField] private float _revealBaseRadius = 1f;
        [Tooltip("Радиус кольца у проёма; это же — макс-радиус пред-инстанса верхних тайлов.")]
        [SerializeField] private float _revealMaxRadius = 4f;
        [Tooltip("Дистанция игрок↔проём, дальше которой радиус = base; ближе → растёт к max.")]
        [SerializeField] private float _revealProximityDistance = 5f;
        [Tooltip("Глубина многоэтажного reveal (этажей вверх/вниз через стопку дыр).")]
        [SerializeField] private int _revealMaxFloors = 3;
        [Tooltip("Сдвиг кольца на этаж глубже, колец/этаж (idx = cheb + floorStep*это).")]
        [SerializeField] private float _revealDepthDim = 1f;
        [Tooltip("Единая непрозрачность стен в reveal (стены шахты одной плотностью, без cheb/глубины).")]
        [Range(0f, 1f)] [SerializeField] private float _wallRevealAlpha = 0.75f;

        [Header("Тестовая карта (для отладки без сервера)")]
        [Tooltip("Относительный путь к .smap для загрузки при старте. Пусто = не грузить.")]
        [SerializeField] private string _testMapPath = "";

        private GridMap _map;

        // Корень-объект на каждый чанк; ключ тот же, что в GridMap (включает z).
        private readonly Dictionary<long, GameObject> _chunkRoots = new();

        // Дедуп чанков, перерисованных за один RefreshTileAt (поле — без аллокаций на вызов).
        private readonly HashSet<long> _refreshedChunks = new();

        // Кандидатный набор reveal-слоёв: (wx,wy,z) → ближайший проём этого уровня + Chebyshev + глубина.
        // Пересчёт в RecomputeReveal (многоуровневая пропагация дыр); используется в ApplyChunk.
        private readonly Dictionary<(int, int, int), CeilingCandidate> _ceilingCandidates = new();

        // Реестр пред-инстансных верхних рендеров для per-frame reveal (динамический радиус, без ребилда).
        // Заполняется в ApplyChunk (delta>0), чистится в ClearAll и PruneCeilingTiles (ребилд чанка).
        private readonly List<CeilingReveal> _ceilingTiles = new();

        private struct CeilingCandidate { public int Hx, Hy, Cheb, Lz, FloorStep, ClusterId; public bool IsWall; }

        private struct CeilingReveal
        {
            public Renderer Renderer;
            public GameObject Root;          // чанковый корень — для прунинга при ребилде
            public bool IsSprite;
            public int Cheb;                 // Chebyshev тайл→ближайший проём
            public float HxCenter, HyCenter; // центр проёма (hx+0.5, hy+0.5)
            public Color BaseColor;          // базовый цвет; alpha задаётся per-frame
            public Texture BaseTex;
            public int Wx, Wy;               // координата тайла (для editor-пробника)
            public int Lz;                   // z-уровень тайла
            public int FloorStep;            // глубина − база направления (вверх 0,1,2; вниз 0,1) — для alpha
            public int ClusterId;            // связный проём (кластер дыр) — гейт reveal по близости к нему
            public bool IsWall;              // глухая стена reveal-уровня → единая alpha (_wallRevealAlpha)
            public bool Opaque;              // непрозрачная стена (alpha≥1): без fade-материала, видимость гейтом
        }

        private MaterialPropertyBlock _mpb;
        private bool _warnedNoFadeMat;
        private bool _warnedNoFadeRing;

        // Shadowcast-буфер «света от дыры» (fix10): кандидаты reveal = тайлы, видимые из дыры на её уровне.
        // Переиспользуется в RecomputeReveal (не per-frame); ресайз только при смене радиуса.
        private float[,] _holeLight;
        private int _holeLightRadius = -1;

        // Кластеры дыр (связные проёмы): центры дыр на кластер + per-frame min-дистанция игрока до кластера.
        // Строятся в RecomputeReveal (не per-frame); _clusterMinDist переиспользуется.
        private int _clusterCount;
        private readonly List<List<Vector2>> _clusterCenters = new();
        private float[] _clusterMinDist;

        // Последняя player-позиция — кэш для editor-диагностики reveal (DescribeReveal); в рантайме безвредно.
        private float _lastPx, _lastPy;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        // Грани тайла (шейдер TileFaceSprites): спрайты боковой/верхней грани через per-tile MPB (без материала безвредно).
        private static readonly int SideTexId = Shader.PropertyToID("_SideTex");
        private static readonly int TopTexId = Shader.PropertyToID("_TopTex");
        private static readonly int SideStId = Shader.PropertyToID("_SideST");
        private static readonly int TopStId = Shader.PropertyToID("_TopST");
        private static readonly int BoundsMinId = Shader.PropertyToID("_BoundsMin");
        private static readonly int BoundsSizeId = Shader.PropertyToID("_BoundsSize");
        private MaterialPropertyBlock _faceMpb;

        private void Start()
        {
            if (!string.IsNullOrEmpty(_testMapPath))
                LoadLocal(_testMapPath);
        }

        // Живая подстройка просвечивания в Play Mode: меняешь тумблеры/кольца — перерисовываем.
        private void OnValidate()
        {
            // Предсказуемый тюнинг в инспекторе: base ≤ max, дистанция > 0 (защита деления в reveal).
            _revealMaxRadius = Mathf.Max(0f, _revealMaxRadius);
            _revealBaseRadius = Mathf.Clamp(_revealBaseRadius, 0f, _revealMaxRadius);
            _revealProximityDistance = Mathf.Max(0.0001f, _revealProximityDistance);
            _revealMaxFloors = Mathf.Max(1, _revealMaxFloors);
            _revealDepthDim = Mathf.Max(0f, _revealDepthDim); // >1 разрешён для тюнинга

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
            int mf = Mathf.Max(1, _revealMaxFloors);
            foreach (var chunk in _map.Chunks)
                if (chunk.Z >= _activeZ - mf && chunk.Z <= _activeZ + mf)
                    ApplyChunk(chunk);
        }

        /// <summary>Сменить активный этаж и перерисовать (переходы по лестнице/лифту).</summary>
        public void SetActiveZ(int z)
        {
            if (_activeZ == z) return;
            _activeZ = z;
            if (_map != null) SetMap(_map);
        }

        /// <summary>Перерисовать после рантайм-изменения тайла (TileUpdate: дверь, смена/снос стены).
        /// Форма стены зависит от 4 прямых соседей (autotiling, W3), а они могут лежать в смежных
        /// чанках — поэтому перерисовываем чанк тайла И его 4 соседей с дедупом. Маску просвечивания не
        /// трогаем: дверь её не меняет, правки структуры пола идут через SetMap.</summary>
        public void RefreshTileAt(int x, int y, int z)
        {
            if (_map == null || z < _activeZ - 1 || z > _activeZ + 1) return;
            _refreshedChunks.Clear();
            ReapplyChunkAt(x, y, z);
            ReapplyChunkAt(x, y + 1, z); ReapplyChunkAt(x, y - 1, z);
            ReapplyChunkAt(x + 1, y, z); ReapplyChunkAt(x - 1, y, z);
        }

        // Перерисовать чанк, содержащий (x,y,z) — не более одного раза за вызов RefreshTileAt (дедуп).
        private void ReapplyChunkAt(int x, int y, int z)
        {
            int cx = FloorDiv(x, Chunk.Size);
            int cy = FloorDiv(y, Chunk.Size);
            if (!_refreshedChunks.Add(Key(cx, cy, z))) return; // чанк уже пересобран в этом вызове
            var chunk = _map.GetChunk(cx, cy, z);
            if (chunk != null) ApplyChunk(chunk);
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>Поворот двери по соседям-структурам: коннекторы восток-запад → поворот на 90°.</summary>
        private Quaternion DoorRotation(int x, int y, int z)
        {
            if (_map == null) return Quaternion.identity;
            bool ew = IsConnector(x - 1, y, z) || IsConnector(x + 1, y, z);
            bool ns = IsConnector(x, y - 1, z) || IsConnector(x, y + 1, z);
            if (!ns && ew) return Quaternion.Euler(0f, 90f, 0f);
            return Quaternion.identity;
        }

        // Сосед-коннектор для ориентации двери: любая структура (стена/окно/дверь/люк), вкл. открытые.
        private bool IsConnector(int x, int y, int z) => _map.GetTile(x, y, z).StructureType != 0;

        /// <summary>Пересчитать многоуровневые reveal-кандидаты: вверх z+1…z+max и вниз z-2…z-max через стопки
        /// вертикальных дыр (z-1 непрозрачен — база вниз). Для каждого тайла: ближайший проём своего уровня +
        /// Chebyshev + глубина (FloorStep для alpha). Непрозрачность считается per-frame в UpdateCeilingReveal.</summary>
        private void RecomputeReveal()
        {
            _ceilingCandidates.Clear();
            _clusterCount = 0;
            if (_map == null) return;

            int maxR = Mathf.Max(0, Mathf.CeilToInt(_revealMaxRadius)); // ceil: дробный max не теряет внешнее кольцо
            int maxFloors = Mathf.Max(1, _revealMaxFloors);

            PropagateUp(maxR, maxFloors);
            PropagateDown(maxR, maxFloors);
        }

        // Вверх: якорь — ПОЛНАЯ вертикальная шахта дыр. reveal z+d вокруг H_d = (дыры z+1)∩…∩(дыры z+d):
        // пересекаем с дырами z+d ДО раскрытия → z+d рисуется только там, где над дырой z+1 есть СВОЯ дыра.
        private void PropagateUp(int maxR, int maxFloors)
        {
            var shaft = CollectUpSeedOpenings(maxFloors);  // H_1: дыры z+1 над полом игрока (с этажом сверху)
            for (int d = 1; d <= maxFloors; d++)
            {
                shaft = HolesAtLevel(shaft, _activeZ + d);  // H_d = шахта ∩ {дыры z+d} — СНАЧАЛА
                if (shaft.Count == 0) break;
                EmitClusteredRings(shaft, _activeZ + d, floorStep: d - 1, maxR); // reveal z+d по связным проёмам
            }
        }

        // Вниз: якорь — ПОЛНАЯ шахта дыр в полу. reveal z-d вокруг H_d = (дыры пола z-1)∩…∩(дыры пола z-d):
        // пересекаем с дырами z-d ДО раскрытия; z-1 непрозрачен (база), кольца с z-2.
        private void PropagateDown(int maxR, int maxFloors)
        {
            if (maxFloors < 2) return;
            var shaft = CollectDownSeedOpenings(maxR, maxFloors); // дыры пола z-1 под дырой пола activeZ (с этажом снизу)
            for (int d = 2; d <= maxFloors; d++)
            {
                shaft = HolesAtLevel(shaft, _activeZ - d);  // H_d = шахта ∩ {дыры пола z-d} — СНАЧАЛА
                if (shaft.Count == 0) break;
                EmitClusteredRings(shaft, _activeZ - d, floorStep: d - 2, maxR); // reveal z-d по связным проёмам
            }
        }

        // Проёмы потолка на activeZ+1: вертикальная дыра над полом игрока (как R1) И над ней есть хотя бы один
        // сплошной этаж в пределах maxFloors (иначе колонна открыта в космос — не reveal, Bug1).
        private List<(int, int)> CollectUpSeedOpenings(int maxFloors)
        {
            var openings = new List<(int, int)>();
            foreach (var chunk in _map.Chunks)
            {
                if (chunk.Z != _activeZ + 1) continue;
                int bx = chunk.ChunkX * Chunk.Size, by = chunk.ChunkY * Chunk.Size;
                for (int ly = 0; ly < Chunk.Size; ly++)
                    for (int lx = 0; lx < Chunk.Size; lx++)
                    {
                        int hx = bx + lx, hy = by + ly;
                        if (_map.GetTile(hx, hy, _activeZ + 1).BlocksVerticalSight) continue;
                        if (_map.GetTile(hx, hy, _activeZ).FloorType == 0) continue; // не космос снаружи станции
                        if (!HasSolidAbove(hx, hy, maxFloors)) continue;             // над дырой нет этажа (космос)
                        openings.Add((hx, hy));
                    }
            }
            return openings;
        }

        // Есть ли сплошной (BlocksVerticalSight) уровень над (x,y) в [activeZ+2 .. activeZ+maxFloors].
        private bool HasSolidAbove(int x, int y, int maxFloors)
        {
            for (int z = _activeZ + 2; z <= _activeZ + maxFloors; z++)
                if (_map.GetTile(x, y, z).BlocksVerticalSight) return true;
            return false;
        }

        // База вниз: дыры в полу z-1, попадающие под дыры пола activeZ (в пределах maxR), И под ними есть хотя
        // бы один сплошной этаж в пределах maxFloors (иначе колонна открыта в космос снизу — не reveal, Bug1).
        private List<(int, int)> CollectDownSeedOpenings(int maxR, int maxFloors)
        {
            var openings = new List<(int, int)>();
            var seen = new HashSet<(int, int)>();
            foreach (var chunk in _map.Chunks)
            {
                if (chunk.Z != _activeZ) continue;
                int bx = chunk.ChunkX * Chunk.Size, by = chunk.ChunkY * Chunk.Size;
                for (int ly = 0; ly < Chunk.Size; ly++)
                    for (int lx = 0; lx < Chunk.Size; lx++)
                    {
                        int hx = bx + lx, hy = by + ly;
                        if (_map.GetTile(hx, hy, _activeZ).BlocksVerticalSight) continue; // пол activeZ не пропускает вниз
                        for (int dy = -maxR; dy <= maxR; dy++)
                            for (int dx = -maxR; dx <= maxR; dx++)
                            {
                                int nx = hx + dx, ny = hy + dy;
                                if (!seen.Add((nx, ny))) continue;
                                if (_map.GetTile(nx, ny, _activeZ - 1).BlocksVerticalSight) continue; // z-1 не дыра
                                if (!HasSolidBelow(nx, ny, maxFloors)) continue;                      // под дырой нет этажа (космос)
                                openings.Add((nx, ny));
                            }
                    }
            }
            return openings;
        }

        // Есть ли сплошной (BlocksVerticalSight) уровень под (x,y) в [activeZ-2 .. activeZ-maxFloors].
        private bool HasSolidBelow(int x, int y, int maxFloors)
        {
            for (int z = _activeZ - 2; z >= _activeZ - maxFloors; z--)
                if (_map.GetTile(x, y, z).BlocksVerticalSight) return true;
            return false;
        }

        // Разбить дыры уровня lz на связные проёмы (кардинальная 4-смежность); от каждого проёма-кластера
        // разложить кольца maxR. Кластеризация — на RecomputeReveal (не per-frame).
        private void EmitClusteredRings(List<(int, int)> shaft, int lz, int floorStep, int maxR)
        {
            var inShaft = new HashSet<(int, int)>(shaft);
            var visited = new HashSet<(int, int)>();
            var stack = new Stack<(int, int)>();
            var component = new List<(int, int)>();

            foreach (var start in shaft)
            {
                if (!visited.Add(start)) continue;
                component.Clear();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    component.Add((cx, cy));
                    FloodVisit(cx - 1, cy, inShaft, visited, stack);
                    FloodVisit(cx + 1, cy, inShaft, visited, stack);
                    FloodVisit(cx, cy - 1, inShaft, visited, stack);
                    FloodVisit(cx, cy + 1, inShaft, visited, stack);
                }

                int clusterId = _clusterCount++;
                var centers = NextClusterCenters(clusterId);
                centers.Clear();
                foreach (var (hx, hy) in component) centers.Add(new Vector2(hx + 0.5f, hy + 0.5f));

                AddCandidatesAround(component, lz, floorStep, maxR, clusterId);
            }
        }

        private static void FloodVisit(int x, int y, HashSet<(int, int)> inShaft, HashSet<(int, int)> visited, Stack<(int, int)> stack)
        {
            var p = (x, y);
            if (inShaft.Contains(p) && visited.Add(p)) stack.Push(p);
        }

        // Переиспользуемый список центров дыр для кластера clusterId (растёт по мере надобности).
        private List<Vector2> NextClusterCenters(int clusterId)
        {
            while (_clusterCenters.Count <= clusterId) _clusterCenters.Add(new List<Vector2>());
            return _clusterCenters[clusterId];
        }

        // Кандидаты reveal от каждой дыры кластера = shadowcast-Fov ОТ дыры на её уровне lz (fix10): стены/углы
        // уровня lz дают тень — за ними не кандидаты; сама стена-граница подсвечена (виден край) → рисуется.
        private void AddCandidatesAround(List<(int, int)> holes, int lz, int floorStep, int maxR, int clusterId)
        {
            EnsureHoleLight(maxR);
            foreach (var (ox, oy) in holes)
            {
                Fov.Compute(_map, ox, oy, lz, maxR, _holeLight); // тот же шедоукастер; наблюдатель — дыра
                for (int dy = -maxR; dy <= maxR; dy++)
                    for (int dx = -maxR; dx <= maxR; dx++)
                    {
                        if (_holeLight[dx + maxR, dy + maxR] <= 0f) continue; // в тени стены/угла на уровне lz
                        int wx = ox + dx, wy = oy + dy;
                        int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                        AddCandidate(wx, wy, lz, ox, oy, cheb, floorStep, clusterId);
                    }
            }
        }

        // (Ре)аллокация буфера «света от дыры» — только при изменении радиуса.
        private void EnsureHoleLight(int radius)
        {
            if (_holeLight != null && _holeLightRadius == radius) return;
            int size = 2 * radius + 1;
            _holeLight = new float[size, size];
            _holeLightRadius = radius;
        }

        // Оставить точки шахты, где на уровне lz — вертикальная дыра (шахта продолжается сквозь этот уровень).
        private List<(int, int)> HolesAtLevel(List<(int, int)> set, int lz)
        {
            var next = new List<(int, int)>();
            foreach (var p in set)
                if (!_map.GetTile(p.Item1, p.Item2, lz).BlocksVerticalSight) next.Add(p);
            return next;
        }

        // При перекрытии колец выигрывает МЕНЬШИЙ Chebyshev (ближайшая дыра); ClusterId следует за якорь-дырой.
        private void AddCandidate(int wx, int wy, int lz, int hx, int hy, int cheb, int floorStep, int clusterId)
        {
            var k = (wx, wy, lz);
            if (_ceilingCandidates.TryGetValue(k, out var cur) && cur.Cheb <= cheb) return; // min-cheb: ближайшая дыра
            _ceilingCandidates[k] = new CeilingCandidate
            {
                Hx = hx, Hy = hy, Cheb = cheb, Lz = lz, FloorStep = floorStep, ClusterId = clusterId,
                IsWall = IsRevealWall(wx, wy, lz) // глухая стена → единая alpha (Bug2)
            };
        }

        // Глухая стена на reveal-тайле (для единой alpha). RecomputeReveal, не per-frame — каталог-lookup ок.
        private bool IsRevealWall(int x, int y, int z)
        {
            if (_catalog == null) return false;
            var t = _map.GetTile(x, y, z);
            if (t.StructureType == 0) return false;
            var s = _catalog.GetStructure(t.StructureType);
            return s != null && s.Category == StructureCategory.Wall;
        }

        // Opacity кольца: ФИКС-индекс = cheb (до ближайшей дыры) + сдвиг глубины (floorStep*_revealDepthDim колец/этаж).
        // НЕ делим на radius — стоя В шахте кольца не пропадают; radius лишь гейтит, сколько колец видно. floorStep=0 → idx=cheb.
        private float RevealAlpha(int cheb, float radius, int floorStep)
        {
            if (cheb > radius) return 0f;
            if (_fadeRingOpacity == null || _fadeRingOpacity.Length == 0) return 0f;
            if (_fadeRingOpacity.Length == 1) return _fadeRingOpacity[0];

            float fidx = cheb + floorStep * _revealDepthDim;
            if (fidx <= 0f) return _fadeRingOpacity[0];
            int i0 = Mathf.FloorToInt(fidx);
            if (i0 >= _fadeRingOpacity.Length - 1) return _fadeRingOpacity[_fadeRingOpacity.Length - 1];
            return Mathf.Lerp(_fadeRingOpacity[i0], _fadeRingOpacity[i0 + 1], fidx - i0);
        }

        // Стены reveal — единая alpha (_wallRevealAlpha), без cheb/глубины; пол/потолок — градиент (fix4).
        private float WallOrRingAlpha(bool isWall, int cheb, float radius, int floorStep)
            => isWall ? _wallRevealAlpha : RevealAlpha(cheb, radius, floorStep);

        /// <summary>Per-frame: подстроить непрозрачность пред-инстансных верхних тайлов под близость игрока к
        /// «своему» проёму. Без ребилда чанков и без аллокаций (реестр и _mpb — поля). Только при reveal.</summary>
        public void UpdateCeilingReveal(float px, float py)
        {
            _lastPx = px; _lastPy = py; // кэш для editor-диагностики (DescribeReveal)
            if (!_drawCeilingReveal) return;
            int n = _ceilingTiles.Count;
            if (n == 0) return;

            // Пустой градиент = reveal фактически выключен: прячем тайлы (без overdraw alpha=0) и выходим.
            if (_fadeRingOpacity == null || _fadeRingOpacity.Length == 0)
            {
                WarnNoFadeRing();
                for (int i = 0; i < n; i++)
                {
                    var rr = _ceilingTiles[i].Renderer;
                    if (rr != null && rr.enabled) rr.enabled = false;
                }
                return;
            }

            _mpb ??= new MaterialPropertyBlock();
            float pd = _revealProximityDistance <= 0f ? 0.0001f : _revealProximityDistance;

            UpdateClusterDistances(px, py); // min-дистанция игрока до каждого связного проёма (по всем его дырам)

            for (int i = 0; i < n; i++)
            {
                var rec = _ceilingTiles[i];
                var r = rec.Renderer;
                if (r == null) continue; // рендер уничтожен ребилдом — пропуск (PruneCeilingTiles чистит реестр)

                // Близость к ВСЕМУ проёму (кластеру дыр), а не к одной якорь-дыре: проём зажигается целиком.
                float dist = rec.ClusterId < _clusterCount ? _clusterMinDist[rec.ClusterId] : float.MaxValue;
                float prox = Mathf.Clamp01((pd - dist) / pd);
                float radius = Mathf.Lerp(_revealBaseRadius, _revealMaxRadius, prox);

                if (rec.Cheb > radius)
                {
                    if (r.enabled) r.enabled = false; // за кольцом — скрыть (без overdraw)
                    continue;
                }
                if (!r.enabled) r.enabled = true;

                if (rec.Opaque) continue; // непрозрачная стена: только видимость, материал/alpha не трогаем

                float a = WallOrRingAlpha(rec.IsWall, rec.Cheb, radius, rec.FloorStep);
                if (rec.IsSprite)
                {
                    var sr = (SpriteRenderer)r;
                    Color col = rec.BaseColor; col.a = a; sr.color = col;
                }
                else
                {
                    ApplyCeilingMpb(r, rec.BaseColor, rec.BaseTex, a);
                }
            }
        }

        // Per-frame: min-дистанция игрока до каждого кластера (по всем его дырам). Массив/центры — поля.
        private void UpdateClusterDistances(float px, float py)
        {
            EnsureClusterDistArray(_clusterCount);
            for (int c = 0; c < _clusterCount; c++)
            {
                var centers = _clusterCenters[c];
                float best = float.MaxValue;
                for (int j = 0; j < centers.Count; j++)
                {
                    float ddx = px - centers[j].x, ddy = py - centers[j].y;
                    float d2 = ddx * ddx + ddy * ddy;
                    if (d2 < best) best = d2;
                }
                _clusterMinDist[c] = best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
            }
        }

        private void EnsureClusterDistArray(int count)
        {
            if (_clusterMinDist == null || _clusterMinDist.Length < count)
                _clusterMinDist = new float[Mathf.Max(count, 4)];
        }

        /// <summary>Пересобрать инстансы тайлов одного чанка (слой по chunk.Z относительно активного).</summary>
        public void ApplyChunk(Chunk chunk)
        {
            if (_catalog == null)
            {
                Debug.LogError("[MapRenderer] TileCatalog не назначен — нечем рисовать тайлы.");
                return;
            }
            if (_map == null) return; // косвенно дёргаем _map через *Connectivity.ResolveAt — защита

            int mf = Mathf.Max(1, _revealMaxFloors);
            int delta = chunk.Z - _activeZ;
            if (delta < -mf || delta > mf) return;

            long key = Key(chunk.ChunkX, chunk.ChunkY, chunk.Z);
            if (_chunkRoots.TryGetValue(key, out var old) && old != null)
            {
                if (_ceilingTiles.Count > 0) PruneCeilingTiles(old); // до Destroy: убрать записи старого чанка
                Destroy(old);
            }

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

                    // Активный этаж и низ — непрозрачно. Верх — либо динамическое кольцо у проёма (R1),
                    // либо весь пол в режиме рентгена; остальной потолок не рисуем (срез — видно свой этаж).
                    float alpha;
                    bool registerCeiling = false;
                    CeilingCandidate cand = default;
                    if (delta == 0 || delta == -1)
                    {
                        alpha = 1f; // активный этаж и z-1 — непрозрачно (z-1 = база вниз)
                    }
                    else
                    {
                        // reveal-уровень: вверх delta>=1, вниз delta<=-2.
                        if (!_drawCeilingReveal) continue;
                        if (_ceilingCandidates.TryGetValue((wx, wy, chunk.Z), out cand))
                        {
                            // Стартовая alpha по базовому радиусу + глубине (стены — единая); обновляется per-frame.
                            alpha = WallOrRingAlpha(cand.IsWall, cand.Cheb, _revealBaseRadius, cand.FloorStep);
                            registerCeiling = true;
                        }
                        else if (delta == 1 && _ceilingSemiTransparent && t.FloorType != 0)
                        {
                            alpha = _ceilingXrayAlpha; // рентген только для z+1 (как раньше)
                        }
                        else continue;
                    }

                    // Структуру тайла берём один раз — переиспользуем в floor-skip (F1) и structure-ветке.
                    var s = t.StructureType != 0 ? _catalog.GetStructure(t.StructureType) : null;
                    bool wallHere = s != null && s.Category == StructureCategory.Wall;

                    if (t.FloorType != 0 && !wallHere) // F1: под стеной пол не рисуем (окна/двери — рисуем)
                    {
                        var f = _catalog.GetFloor(t.FloorType);
                        GameObject floorGo;

                        // F2: пол с autotiling выбирает форму по 4 соседям-полам → меш + поворот по Unity-Y.
                        if (f != null && f.Connection.UseConnections)
                        {
                            var (shape, steps) = FloorConnectivity.ResolveAt(_catalog, _map, f, wx, wy, chunk.Z);
                            var mesh = f.Connection.GetMesh(shape);
                            var prefab = mesh != null ? mesh : f.Prefab; // фолбэк, если меш формы не назначен
                            // Плоский спрайт-фолбэк (ни меша, ни Prefab) по Y не крутим — встал бы ребром.
                            var rot = prefab != null ? Quaternion.Euler(0f, 90f * steps, 0f) : Quaternion.identity;
                            // planarScale=1: у autotiling-мешей края задаёт сам меш, seam-scale не нужен.
                            floorGo = Spawn(prefab, f.Sprite, wx, wy, floorBaseY + _floorYOffset, _floorSortingOrder,
                                root.transform, "Floor", alignTop: true, planarScale: 1f, rotation: rot, alpha: alpha, deferFade: registerCeiling);
                            ApplyFaceSprites(floorGo, f.SideSprite, f.Connection.GetTopSprite(shape));
                        }
                        else
                        {
                            float ps = alpha < 1f ? 1f : _floorSeamScale;
                            floorGo = Spawn(f?.Prefab, f?.Sprite, wx, wy, floorBaseY + _floorYOffset, _floorSortingOrder,
                                root.transform, "Floor", alignTop: true, planarScale: ps, rotation: Quaternion.identity, alpha: alpha, deferFade: registerCeiling);
                            if (f != null) ApplyFaceSprites(floorGo, f.SideSprite, f.TopSprite);
                        }

                        if (registerCeiling) RegisterCeiling(floorGo, root, cand, wx, wy);
                    }

                    if (t.StructureType != 0)
                    {
                        bool openNow = s != null && s.Openable && t.Open;
                        GameObject structGo;

                        // Глухая стена с autotiling: форма по 4 прямым соседям → меш + поворот по Unity-Y.
                        if (s != null && !openNow && s.Category == StructureCategory.Wall && s.Connection.UseConnections)
                        {
                            var (shape, steps) = WallConnectivity.ResolveAt(_catalog, _map, s, wx, wy, chunk.Z);
                            var mesh = s.Connection.GetMesh(shape);
                            var prefab = mesh != null ? mesh : s.Prefab; // фолбэк, если меш формы не назначен
                            // Плоский спрайт-фолбэк (ни меша, ни Prefab) по Y не крутим — встал бы ребром к камере.
                            var rot = prefab != null ? Quaternion.Euler(0f, 90f * steps, 0f) : Quaternion.identity;
                            structGo = Spawn(prefab, s.Sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder,
                                root.transform, "Structure", alignTop: false, planarScale: 1f, rotation: rot, alpha: alpha, deferFade: registerCeiling);
                            ApplyFaceSprites(structGo, s.SideSprite, s.Connection.GetTopSprite(shape));
                        }
                        else
                        {
                            var prefab = openNow ? s.OpenPrefab : s?.Prefab;
                            var sprite = openNow ? s.OpenSprite : s?.Sprite;
                            // Открываемые (двери/люки) поворачиваем по линии стены; глухие — как есть.
                            var rot = (s != null && s.Openable) ? DoorRotation(wx, wy, chunk.Z) : Quaternion.identity;
                            structGo = Spawn(prefab, sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder,
                                root.transform, "Structure", alignTop: false, planarScale: 1f, rotation: rot, alpha: alpha, deferFade: registerCeiling);
                            if (s != null) ApplyFaceSprites(structGo, s.SideSprite, s.TopSprite);
                        }

                        if (registerCeiling) RegisterCeiling(structGo, root, cand, wx, wy);
                    }
                }
            }
        }

        /// <summary>Создать инстанс тайла (префаб или fallback-спрайт) в центре тайла, выровнять по грани,
        /// при alpha&lt;1 — погасить через прозрачный материал. Возвращает созданный объект (null — пусто).
        /// deferFade=true: фейд НЕ применяем (его сделает RegisterCeiling, перехватив базовый цвет/текстуру).</summary>
        private GameObject Spawn(GameObject prefab, Sprite sprite, int wx, int wy, float y, int sortingOrder,
            Transform parent, string label, bool alignTop, float planarScale, Quaternion rotation, float alpha = 1f, bool deferFade = false)
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
                // При deferFade оставляем полную непрозрачность — alpha задаст RegisterCeiling.
                var col = sr.color; col.a = deferFade ? 1f : alpha; sr.color = col;
            }
            else
            {
                return null; // ни префаба, ни спрайта
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
            if (!deferFade && alpha < 1f && prefab != null) ApplyFade(renderers, alpha);
            return go;
        }

        // Зарегистрировать рендеры верхнего тайла для динамического reveal: перехватить базовый цвет/текстуру,
        // подменить материал на прозрачный (как ApplyFade) и выставить стартовую alpha по базовому радиусу.
        private void RegisterCeiling(GameObject go, GameObject root, in CeilingCandidate cand, int wx, int wy)
        {
            if (go == null) return;
            bool opaque = cand.IsWall && _wallRevealAlpha >= 1f; // непрозрачная стена → без fade-материала
            float hxC = cand.Hx + 0.5f, hyC = cand.Hy + 0.5f;
            float startAlpha = WallOrRingAlpha(cand.IsWall, cand.Cheb, _revealBaseRadius, cand.FloorStep);

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer sr)
                {
                    Color baseC = sr.color; baseC.a = 1f;
                    _ceilingTiles.Add(new CeilingReveal
                    {
                        Renderer = r, Root = root, IsSprite = true, Cheb = cand.Cheb,
                        HxCenter = hxC, HyCenter = hyC, BaseColor = baseC, BaseTex = null,
                        Wx = wx, Wy = wy, Lz = cand.Lz, FloorStep = cand.FloorStep, ClusterId = cand.ClusterId, IsWall = cand.IsWall, Opaque = opaque
                    });
                    if (!opaque) { var col = baseC; col.a = startAlpha; sr.color = col; } // opaque — alpha остаётся 1
                    continue;
                }

                if (opaque)
                {
                    // Непрозрачная стена: оставляем исходный материал (без fade-свапа), видимость — гейтом близости.
                    _ceilingTiles.Add(new CeilingReveal
                    {
                        Renderer = r, Root = root, IsSprite = false, Cheb = cand.Cheb,
                        HxCenter = hxC, HyCenter = hyC, BaseColor = Color.white, BaseTex = null,
                        Wx = wx, Wy = wy, Lz = cand.Lz, FloorStep = cand.FloorStep, ClusterId = cand.ClusterId, IsWall = cand.IsWall, Opaque = true
                    });
                    continue;
                }

                if (_floorFadeMaterial == null) { WarnNoFadeMat(); continue; }

                var orig = r.sharedMaterial;
                Color c = (orig != null && orig.HasProperty(BaseColorId)) ? orig.GetColor(BaseColorId) : Color.white;
                Texture tex = (orig != null && orig.HasProperty(BaseMapId)) ? orig.GetTexture(BaseMapId) : null;
                c.a = 1f;

                SwapRendererToFade(r);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // прозрачный потолок не льёт тень

                _ceilingTiles.Add(new CeilingReveal
                {
                    Renderer = r, Root = root, IsSprite = false, Cheb = cand.Cheb,
                    HxCenter = hxC, HyCenter = hyC, BaseColor = c, BaseTex = tex,
                    Wx = wx, Wy = wy, Lz = cand.Lz, FloorStep = cand.FloorStep, ClusterId = cand.ClusterId, IsWall = cand.IsWall, Opaque = false
                });
                ApplyCeilingMpb(r, c, tex, startAlpha);
            }
        }

        // Убрать из реестра записи, принадлежащие чанку root (вызывать ДО Destroy(root)).
        private void PruneCeilingTiles(GameObject root)
        {
            for (int i = _ceilingTiles.Count - 1; i >= 0; i--)
                if (_ceilingTiles[i].Root == root) _ceilingTiles.RemoveAt(i);
        }

        // Погасить меш-рендеры объекта: подменить материал на прозрачный, перенести текстуру/цвет
        // оригинала с заданной alpha через MaterialPropertyBlock (без аллокаций материалов).
        private void ApplyFade(Renderer[] renderers, float alpha)
        {
            if (_floorFadeMaterial == null) { WarnNoFadeMat(); return; }

            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue; // спрайтам alpha уже задана через color

                var orig = r.sharedMaterial;
                Color c = (orig != null && orig.HasProperty(BaseColorId)) ? orig.GetColor(BaseColorId) : Color.white;
                Texture tex = (orig != null && orig.HasProperty(BaseMapId)) ? orig.GetTexture(BaseMapId) : null;

                SwapRendererToFade(r);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // прозрачный потолок не льёт тень
                ApplyCeilingMpb(r, c, tex, alpha);
            }
        }

        // Подменить материал рендера на _floorFadeMaterial на всех сабмешах (иначе мультиматериальный
        // префаб останется частично непрозрачным).
        private void SwapRendererToFade(Renderer r)
        {
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
        }

        // Выставить непрозрачность рендера через переиспользуемый MaterialPropertyBlock (без аллокаций).
        private void ApplyCeilingMpb(Renderer r, Color baseColor, Texture baseTex, float alpha)
        {
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear(); // иначе _BaseMap протекает с предыдущего рендера (SetTexture условный)
            Color c = baseColor; c.a = alpha;
            _mpb.SetColor(BaseColorId, c);
            if (baseTex != null) _mpb.SetTexture(BaseMapId, baseTex);
            r.SetPropertyBlock(_mpb);
        }

        // UV-rect спрайта в его текстуре, нормированный (xy=scale, zw=offset). Без спрайта/текстуры → (1,1,0,0).
        private static Vector4 SpriteST(Sprite s)
        {
            if (s == null || s.texture == null) return new Vector4(1f, 1f, 0f, 0f);
            var rect = s.textureRect;
            float tw = s.texture.width, th = s.texture.height;
            return new Vector4(rect.width / tw, rect.height / th, rect.x / tw, rect.y / th);
        }

        // Per-tile спрайты граней через MPB (шейдер TileFaceSprites). Меш-рендеры; спрайт-фолбэк не трогаем.
        // Без материала TileFaceSprites — безвредно (свойств нет → игнор). RecomputeReveal/ApplyChunk, не per-frame.
        private void ApplyFaceSprites(GameObject go, Sprite side, Sprite top)
        {
            if (go == null || (side == null && top == null)) return;
            _faceMpb ??= new MaterialPropertyBlock();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue; // спрайт-фолбэк — другой материал, не трогаем

                _faceMpb.Clear();
                r.GetPropertyBlock(_faceMpb); // сохранить уже выставленный блок (если есть), иначе пусто
                if (side != null && side.texture != null)
                {
                    _faceMpb.SetTexture(SideTexId, side.texture);
                    _faceMpb.SetVector(SideStId, SpriteST(side));
                }
                if (top != null && top.texture != null)
                {
                    _faceMpb.SetTexture(TopTexId, top.texture);
                    _faceMpb.SetVector(TopStId, SpriteST(top));
                }

                // Локальные габариты меша (AABB) → нормировка UV в шейдере по грани. Нет меша → дефолт шейдера.
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh != null)
                {
                    var b = mesh.bounds;
                    _faceMpb.SetVector(BoundsMinId, (Vector4)b.min);
                    _faceMpb.SetVector(BoundsSizeId, (Vector4)b.size);
                }

                r.SetPropertyBlock(_faceMpb);
            }
        }

        private void WarnNoFadeMat()
        {
            if (_warnedNoFadeMat) return;
            Debug.LogWarning("[MapRenderer] _floorFadeMaterial не назначен — полупрозрачность верхнего этажа не работает.");
            _warnedNoFadeMat = true;
        }

        private void WarnNoFadeRing()
        {
            if (_warnedNoFadeRing) return;
            Debug.LogWarning("[MapRenderer] _fadeRingOpacity пуст — кольцо просвета выключено (верхние тайлы скрыты).");
            _warnedNoFadeRing = true;
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
            _ceilingTiles.Clear(); // верхние рендеры уничтожены вместе с чанками
        }

        // Та же упаковка ключа, что в GridMap.
        private static long Key(int cx, int cy, int z)
        {
            return ((long)(cx & 0x1FFFFF))
                 | ((long)(cy & 0x1FFFFF) << 21)
                 | ((long)(z & 0x1FFFFF) << 42);
        }

#if UNITY_EDITOR
        // === Editor-only диагностика reveal (см. CeilingRevealProbe). В билд не входит; только ЧИТАЕТ состояние. ===

        /// <summary>Описать reveal-состояние тайла по его Renderer (точная запись реестра, иначе — координаты transform).</summary>
        public string DescribeReveal(Renderer r)
        {
            if (r == null) return "[RevealProbe] Renderer == null";
            for (int i = 0; i < _ceilingTiles.Count; i++)
            {
                var rec = _ceilingTiles[i];
                if (rec.Renderer == r)
                    return $"[RevealProbe] точная запись по Renderer: enabled={r.enabled}, IsSprite={rec.IsSprite}\n"
                         + DescribeReveal(rec.Wx, rec.Wy, rec.Lz);
            }
            var p = r.transform.position;
            int wx = Mathf.FloorToInt(p.x);
            int wy = Mathf.FloorToInt(p.z);
            int lz = RenderConfig.FloorHeight > 0f ? Mathf.RoundToInt(p.y / RenderConfig.FloorHeight) : _activeZ;
            return "[RevealProbe] Renderer не в реестре reveal — координаты из transform (lz приблизительно)\n"
                 + DescribeReveal(wx, wy, lz);
        }

        /// <summary>Полная сводка reveal-состояния тайла (wx,wy,lz) с живым пересчётом гейта и вердиктом-причиной.</summary>
        public string DescribeReveal(int wx, int wy, int lz)
        {
            var sb = new System.Text.StringBuilder();
            int delta = lz - _activeZ;
            sb.AppendLine($"[RevealProbe] tile (wx={wx}, wy={wy}, lz={lz}), delta = lz - activeZ = {delta}");
            sb.AppendLine($"  global: drawCeilingReveal={_drawCeilingReveal}, activeZ={_activeZ}, player=({_lastPx:F2},{_lastPy:F2})");
            sb.AppendLine($"  params: base={_revealBaseRadius}, max={_revealMaxRadius}, prox={_revealProximityDistance}, "
                        + $"maxFloors={_revealMaxFloors}, depthDim={_revealDepthDim}, ringOpacityLen={(_fadeRingOpacity?.Length ?? 0)}");

            bool isCand = _ceilingCandidates.TryGetValue((wx, wy, lz), out var cand);
            if (isCand)
                sb.AppendLine($"  candidate: ДА — Cheb={cand.Cheb}, FloorStep={cand.FloorStep}, Hx={cand.Hx}, Hy={cand.Hy}, ClusterId={cand.ClusterId}");
            else
                sb.AppendLine("  candidate: НЕТ — тайл не кандидат (на этом уровне не спавнится/не регистрируется как reveal)");

            int recIdx = -1;
            for (int i = 0; i < _ceilingTiles.Count; i++)
                if (_ceilingTiles[i].Wx == wx && _ceilingTiles[i].Wy == wy && _ceilingTiles[i].Lz == lz) { recIdx = i; break; }
            if (recIdx >= 0)
            {
                var rec = _ceilingTiles[recIdx];
                bool en = rec.Renderer != null && rec.Renderer.enabled;
                sb.AppendLine($"  record: ДА — Renderer.enabled={en}, IsSprite={rec.IsSprite}, "
                            + $"Cheb={rec.Cheb}, FloorStep={rec.FloorStep}, ClusterId={rec.ClusterId}, HxCenter={rec.HxCenter:F1}, HyCenter={rec.HyCenter:F1}");
            }
            else
            {
                sb.AppendLine("  record: НЕТ — нет записи в _ceilingTiles для этих координат (рендерер не зарегистрирован на reveal)");
            }

            if (isCand)
            {
                float pd = _revealProximityDistance <= 0f ? 0.0001f : _revealProximityDistance;
                float dist = ClusterDistDebug(cand.ClusterId);
                float prox = Mathf.Clamp01((pd - dist) / pd);
                float radius = Mathf.Lerp(_revealBaseRadius, _revealMaxRadius, prox);
                bool gated = cand.Cheb > radius;
                float alpha = WallOrRingAlpha(cand.IsWall, cand.Cheb, radius, cand.FloorStep);

                sb.AppendLine($"  gate(live): clusterMinDist={dist:F2}, prox={prox:F2}, radius={radius:F2}, "
                            + $"cheb={cand.Cheb}, cheb>radius={gated}, alpha={alpha:F3}");

                string verdict;
                if (!_drawCeilingReveal) verdict = "скрыт: drawCeilingReveal=false (reveal выключен)";
                else if (gated) verdict = $"скрыт: cheb({cand.Cheb}) > radius({radius:F2}) (проём далеко / мал радиус — близость)";
                else verdict = $"показан: alpha={alpha:F3}";
                sb.AppendLine($"  ВЕРДИКТ: {verdict}");

                int holes = (cand.ClusterId >= 0 && cand.ClusterId < _clusterCenters.Count) ? _clusterCenters[cand.ClusterId].Count : 0;
                sb.AppendLine($"  cluster: ClusterId={cand.ClusterId}, дыр в кластере={holes}, ближайшая дыра dist={dist:F2}");
            }
            else
            {
                sb.AppendLine("  ВЕРДИКТ: не кандидат — тайл не участвует в reveal на этом уровне");
            }

            AppendMiniMap(sb, wx, wy, lz, isCand, cand);

            return sb.ToString();
        }

        // Min-дистанция от последней player-позиции до дыр кластера (editor-пересчёт; не трогает _clusterMinDist).
        private float ClusterDistDebug(int clusterId)
        {
            if (clusterId < 0 || clusterId >= _clusterCenters.Count) return float.MaxValue;
            var centers = _clusterCenters[clusterId];
            float best = float.MaxValue;
            for (int j = 0; j < centers.Count; j++)
            {
                float ddx = _lastPx - centers[j].x, ddy = _lastPy - centers[j].y;
                float d2 = ddx * ddx + ddy * ddy;
                if (d2 < best) best = d2;
            }
            return best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
        }

        // Editor-only: ASCII мини-карта по z-уровням в окне вокруг probe/player/anchor — где стены/пол/дыры.
        private void AppendMiniMap(System.Text.StringBuilder sb, int wx, int wy, int lz, bool isCand, in CeilingCandidate cand)
        {
            sb.AppendLine("  --- Мини-карта (по уровням) ---");
            if (_map == null) { sb.AppendLine("  (карта не загружена)"); return; }

            int ptx = Mathf.FloorToInt(_lastPx), pty = Mathf.FloorToInt(_lastPy);
            int minX = wx, maxX = wx, minY = wy, maxY = wy;
            minX = Mathf.Min(minX, ptx); maxX = Mathf.Max(maxX, ptx);
            minY = Mathf.Min(minY, pty); maxY = Mathf.Max(maxY, pty);
            if (isCand)
            {
                minX = Mathf.Min(minX, cand.Hx); maxX = Mathf.Max(maxX, cand.Hx);
                minY = Mathf.Min(minY, cand.Hy); maxY = Mathf.Max(maxY, cand.Hy);
            }
            minX -= 4; maxX += 4; minY -= 4; maxY += 4;

            // Прочие дыры кластера probe-тайла (на уровне lz) — для символа 'o'.
            HashSet<(int, int)> clusterHoles = null;
            if (isCand && cand.ClusterId >= 0 && cand.ClusterId < _clusterCenters.Count)
            {
                clusterHoles = new HashSet<(int, int)>();
                var centers = _clusterCenters[cand.ClusterId];
                for (int j = 0; j < centers.Count; j++)
                    clusterHoles.Add((Mathf.FloorToInt(centers[j].x), Mathf.FloorToInt(centers[j].y)));
            }

            int mf = Mathf.Max(1, _revealMaxFloors);
            sb.AppendLine("  легенда: @=игрок T=probe H=дыра o=дыра-кластера #=стена .=пол ' '=открыто(дыра/пустота)");
            sb.AppendLine($"  окно x:[{minX}..{maxX}] y:[{minY}..{maxY}] (строки: сверху y={maxY} (север), вниз y={minY})");

            var line = new System.Text.StringBuilder();
            for (int z = _activeZ - 1; z <= _activeZ + mf; z++)
            {
                sb.AppendLine($"  --- z={z} ---");
                for (int y = maxY; y >= minY; y--)
                {
                    line.Clear();
                    line.Append("  ");
                    for (int x = minX; x <= maxX; x++)
                    {
                        char ch;
                        if (z == _activeZ && x == ptx && y == pty) ch = '@';
                        else if (z == lz && x == wx && y == wy) ch = 'T';
                        else if (z == lz && isCand && x == cand.Hx && y == cand.Hy) ch = 'H';
                        else if (z == lz && clusterHoles != null && clusterHoles.Contains((x, y))) ch = 'o';
                        else
                        {
                            var t = _map.GetTile(x, y, z);
                            if (t.BlocksHorizontalSight) ch = '#';
                            else if (t.FloorType != 0) ch = '.';
                            else ch = ' ';
                        }
                        line.Append(ch);
                    }
                    sb.AppendLine(line.ToString());
                }
            }

            sb.AppendLine($"  столб над probe ({wx},{wy}): BlocksVerticalSight "
                        + $"z={lz - 1}:{_map.GetTile(wx, wy, lz - 1).BlocksVerticalSight}, "
                        + $"z={lz}:{_map.GetTile(wx, wy, lz).BlocksVerticalSight}, "
                        + $"z={lz + 1}:{_map.GetTile(wx, wy, lz + 1).BlocksVerticalSight}");
        }

#endif
    }
}
