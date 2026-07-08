using System.Collections.Generic;
using UnityEngine;
using Shared.World;
using Shared.Simulation;
using Client.Map;
using Client.Config;

namespace Client.Net.View
{
    // Архитектура (Obsidian): RendererPool · CeilingReveal · WallConnection
    /// <summary>Рисует GridMap префабами из TileCatalog в 2.5D: чанки, пул тайлов, автотайлинг, reveal верхних/нижних этажей.</summary>
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

        [Header("Маркеры спец-тайлов (переходы; человек назначает спрайты; Lift — без маркера)")]
        [SerializeField] private Sprite _stairUpMarker;
        [SerializeField] private Sprite _stairDownMarker;
        [SerializeField] private Sprite _spawnMarker;
        [Tooltip("Сдвиг маркера по Y над полом (против z-fighting).")]
        [SerializeField] private float _markerYOffset = 0.002f;
        [SerializeField] private int _markerSortingOrder = 5; // выше пола (0), ниже стен (10)

        [Header("Просвечивание этажей")]
        [Tooltip("Показывать этаж выше кольцами вокруг проёмов в потолке.")]
        [SerializeField] private bool _drawCeilingReveal = true;
        [Tooltip("Рентген: показывать ВЕСЬ верхний этаж полупрозрачным, а не только кольца у проёмов.")]
        [SerializeField] private bool _ceilingSemiTransparent = false;
        [Tooltip("Непрозрачность верхнего этажа в режиме рентгена (0..1).")]
        [Range(0f, 1f)] [SerializeField] private float _ceilingXrayAlpha = 0.4f;
        [Tooltip("Градиент непрозрачности кольца по абсолютному индексу cheb+floorStep·depthDim (не нормируется на радиус).")]
        [SerializeField] private float[] _fadeRingOpacity = { 0f, 0.18f, 0.34f, 0.50f };

        [Header("Динамический просвет потолка (R1)")]
        [Tooltip("Радиус кольца (тайлы), когда игрок далеко от проёма.")]
        [SerializeField] private float _revealBaseRadius = 1f;
        [Tooltip("Радиус кольца у проёма; это же — макс-радиус пред-инстанса верхних тайлов.")]
        [SerializeField] private float _revealMaxRadius = 4f;
        [Tooltip("Дистанция игрок↔проём, дальше которой радиус = base; ближе → растёт к max.")]
        [SerializeField] private float _revealProximityDistance = 5f;
        [Tooltip("Глубина многоэтажного reveal вверх/вниз через стопку дыр (мин. 2 — иначе up-reveal молча гаснет).")]
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

        // (wx,wy,z) → ближайший проём этого уровня + Chebyshev + глубина; пересчёт RecomputeReveal, читает ApplyChunk.
        private readonly Dictionary<(int, int, int), CeilingCandidate> _ceilingCandidates = new();

        // Реестр пред-инстансных верхних рендеров для per-frame reveal (динамический радиус, без ребилда чанков).
        private readonly List<CeilingReveal> _ceilingTiles = new();

        private struct CeilingCandidate { public int Hx, Hy, Cheb, Lz, FloorStep, ClusterId; public bool IsWall; }

        private struct CeilingReveal
        {
            public Renderer Renderer;
            public GameObject Root;          // чанковый корень — для прунинга при ребилде
            public bool IsSprite;
            public int Cheb;                 // Chebyshev тайл→ближайший проём
            public Color BaseColor;          // базовый цвет СПРАЙТА (alpha per-frame); для меша не используется (reveal через _alpha)
            public int Wx, Wy;               // координата тайла (для editor-пробника)
            public int Lz;                   // z-уровень тайла
            public int FloorStep;            // глубина − база направления (вверх 0,1,2; вниз 0,1) — для alpha
            public int ClusterId;            // связный проём (кластер дыр) — гейт reveal по близости к нему
            public bool IsWall;              // глухая стена reveal-уровня → единая alpha (_wallRevealAlpha)
            public bool Opaque;              // непрозрачная стена (alpha≥1): без fade-материала, видимость гейтом
        }

        private MaterialPropertyBlock _mpb;
        private bool _warnedNoFadeRing;

        // Пул тайл-GO: реюз вместо Instantiate/Destroy (стрим грузит/выгружает чанки постоянно — иначе GC-хитчи).
        private Transform _poolRoot;                                                  // неактивный контейнер released-тайлов
        private readonly Dictionary<GameObject, Stack<GameObject>> _tilePool = new();  // по префабу
        private readonly Stack<GameObject> _spritePool = new();                        // sprite-fallback
        private const int MaxPooledPerKey = 512;                                       // кап на стек — дальние чанки не держат память вечно

        // Shadowcast-буфер «света от дыры»: кандидаты reveal = тайлы, видимые из дыры на её уровне.
        private float[,] _holeLight;
        private int _holeLightRadius = -1;

        // Скретч-коллекции пропагации reveal (RecomputeReveal, не per-frame) — без GC на смену этажа.
        private readonly List<(int, int)> _shaft = new();           // текущая вертикальная шахта дыр (Up, затем Down)
        private readonly HashSet<(int, int)> _seedSeen = new();     // дедуп окрестности при сборе нижних сид-дыр
        private readonly HashSet<(int, int)> _inShaft = new();      // membership шахты для flood-fill кластеров
        private readonly HashSet<(int, int)> _clusterVisited = new();
        private readonly Stack<(int, int)> _clusterStack = new();
        private readonly List<(int, int)> _clusterComponent = new();

        // Кластеры дыр (связные проёмы): центры дыр на кластер + per-frame min-дистанция игрока до кластера.
        private int _clusterCount;
        private readonly List<List<Vector2>> _clusterCenters = new();
        private float[] _clusterMinDist;

        // Последняя player-позиция — кэш для editor-диагностики reveal (DescribeReveal) и short-circuit ниже.
        private float _lastPx, _lastPy;

        // Short-circuit: игрок не сдвинулся и reveal-состояние валидно → оба прохода дадут тот же результат, пропуск кадра.
        private bool _revealValid;
        private const float RevealMoveEps = 1e-4f;

        // Стрим: набор загруженных чанков сменился → _ceilingCandidates устарели (RecomputeReveal бежит только в SetMap).
        // Ставится в OnChunkData/OnChunkUnload, гасится раз/кадр в RefreshRevealIfDirty (естественный троттл по Poll).
        private bool _revealDirtyStream;

        // Материал TileReader верха тайла: _TopMap/_DownTex + форма `_cur` + углы `_cur_corner`/`_rotate` (uint → SetInteger).
        // Тело/бок стены (TileWall) — отдельный _sideMpb, `_cur` туда не течёт.
        private static readonly int TopMapId = Shader.PropertyToID("_TopMap");
        private static readonly int DownTexId = Shader.PropertyToID("_DownTex");
        private static readonly int AlphaId = Shader.PropertyToID("_alpha"); // reveal: полупрозрачность верх-этажа (float, per-tile, SetFloat)
        private static readonly int CurId = Shader.PropertyToID("_cur");
        private static readonly int CurCornerId = Shader.PropertyToID("_cur_corner");
        private static readonly int RotateId = Shader.PropertyToID("_rotate");
        private MaterialPropertyBlock _wallMpb;   // верх (WallTop / материал TileTop)
        private MaterialPropertyBlock _sideMpb;   // тело/бок стены (материал TileWall) — изоляция от _cur/_cur_corner/_rotate

        private void Start()
        {
            // Тест-карта — только для standalone без сети; если сеть уже привязала карту (Awake идёт до Start),
            // не перетираем — иначе рендер и коллизия/двери разойдутся на разные GridMap-инстансы.
            if (_map == null && !string.IsNullOrEmpty(_testMapPath))
                LoadLocal(_testMapPath);
        }

        private void OnValidate() // живая подстройка просвечивания в Play Mode
        {
            // Предсказуемый тюнинг в инспекторе: base ≤ max, дистанция > 0 (защита деления в reveal).
            _revealMaxRadius = Mathf.Max(0f, _revealMaxRadius);
            _revealBaseRadius = Mathf.Clamp(_revealBaseRadius, 0f, _revealMaxRadius);
            _revealProximityDistance = Mathf.Max(0.0001f, _revealProximityDistance);
            // Мин. 2: при 1 диапазон HasSolidAbove [activeZ+2..activeZ+1] пуст → up-reveal молча умирал.
            _revealMaxFloors = Mathf.Max(2, _revealMaxFloors);
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
            int mf = Mathf.Max(2, _revealMaxFloors);
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

        /// <summary>Перерисовывает чанк тайла и 4 соседей после рантайм-изменения (TileUpdate).</summary>
        public void RefreshTileAt(int x, int y, int z)
        {
            if (_map == null || z < _activeZ - 1 || z > _activeZ + 1) return;
            _refreshedChunks.Clear();
            ReapplyChunkAt(x, y, z);
            ReapplyChunkAt(x, y + 1, z); ReapplyChunkAt(x, y - 1, z);
            ReapplyChunkAt(x + 1, y, z); ReapplyChunkAt(x - 1, y, z);
        }

        /// <summary>Перерисовывает чанк с (x,y,z) не чаще раза за вызов.</summary>
        private void ReapplyChunkAt(int x, int y, int z)
        {
            int cx = FloorDiv(x, Chunk.Size);
            int cy = FloorDiv(y, Chunk.Size);
            if (!_refreshedChunks.Add(Key(cx, cy, z))) return; // чанк уже пересобран в этом вызове
            var chunk = _map.GetChunk(cx, cy, z);
            if (chunk != null) ApplyChunk(chunk);
        }

        /// <summary>Пере-резолвит autotiling-края 4 соседних чанков вокруг (cx,cy,z).</summary>
        public void ReapplyLoadedNeighbors(int cx, int cy, int z) // плоско, без рекурсии — ApplyChunk не зовём, иначе каскад
        {
            ReapplyChunkIfLoaded(cx + 1, cy, z);
            ReapplyChunkIfLoaded(cx - 1, cy, z);
            ReapplyChunkIfLoaded(cx, cy + 1, z);
            ReapplyChunkIfLoaded(cx, cy - 1, z);
        }

        /// <summary>Пере-применяет чанк, только если он уже отрисован.</summary>
        private void ReapplyChunkIfLoaded(int cx, int cy, int z)
        {
            if (_map == null || !_chunkRoots.ContainsKey(Key(cx, cy, z))) return;
            var chunk = _map.GetChunk(cx, cy, z);
            if (chunk != null) ApplyChunk(chunk);
        }

        /// <summary>Помечает reveal-кандидаты устаревшими (вызывать из OnChunkData/OnChunkUnload).</summary>
        public void MarkRevealDirty() => _revealDirtyStream = true;

        /// <summary>Раз/кадр (если dirty): пересчитывает reveal-кандидаты для догруженных стримом чанков.</summary>
        public void RefreshRevealIfDirty()
        {
            if (!_revealDirtyStream) return;
            _revealDirtyStream = false;
            if (_map == null) return;

            RecomputeReveal();
            int mf = Mathf.Max(2, _revealMaxFloors);
            foreach (var chunk in _map.Chunks) // ApplyChunk не мутирует _map.Chunks (правит только _chunkRoots/тайлы) → обход безопасен
            {
                int d = chunk.Z - _activeZ;
                if ((d >= 1 || d <= -2) && d >= -mf && d <= mf
                    && _chunkRoots.ContainsKey(Key(chunk.ChunkX, chunk.ChunkY, chunk.Z)))
                    ApplyChunk(chunk); // сам prune reveal + release в пул + пере-регистрация reveal по новым кандидатам
            }
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

        private bool IsConnector(int x, int y, int z) => _map.GetTile(x, y, z).StructureType != 0; // любая структура, вкл. открытые

        /// <summary>Пересчитать многоуровневые reveal-кандидаты вверх/вниз через стопки вертикальных дыр.</summary>
        private void RecomputeReveal()
        {
            _ceilingCandidates.Clear();
            _clusterCount = 0;
            _revealValid = false; // состав кандидатов/кластеров изменился → per-frame пройти заново
            if (_map == null) return;

            int maxR = Mathf.Max(0, Mathf.CeilToInt(_revealMaxRadius)); // ceil: дробный max не теряет внешнее кольцо
            int maxFloors = Mathf.Max(2, _revealMaxFloors); // кламп и здесь — билд может нести стейл-значение 1 (OnValidate там не бежит)

            PropagateUp(maxR, maxFloors);
            PropagateDown(maxR, maxFloors);
        }

        /// <summary>Пробрасывает reveal-кандидаты вверх по вертикальным шахтам дыр.</summary>
        private void PropagateUp(int maxR, int maxFloors)
        {
            var shaft = CollectUpSeedOpenings(maxFloors);  // H_1: дыры z+1 над полом игрока (с этажом сверху)
            for (int d = 1; d <= maxFloors; d++)
            {
                FilterHolesInPlace(shaft, _activeZ + d);    // H_d = шахта ∩ {дыры z+d} — на месте, СНАЧАЛА
                if (shaft.Count == 0) break;
                EmitClusteredRings(shaft, _activeZ + d, floorStep: d - 1, maxR); // reveal z+d по связным проёмам
            }
        }

        /// <summary>Пробрасывает reveal-кандидаты вниз по вертикальным шахтам дыр в полу.</summary>
        private void PropagateDown(int maxR, int maxFloors)
        {
            if (maxFloors < 2) return;
            var shaft = CollectDownSeedOpenings(maxR, maxFloors); // дыры пола z-1 под дырой пола activeZ (с этажом снизу)
            for (int d = 2; d <= maxFloors; d++)
            {
                FilterHolesInPlace(shaft, _activeZ - d);    // H_d = шахта ∩ {дыры пола z-d} — на месте, СНАЧАЛА
                if (shaft.Count == 0) break;
                EmitClusteredRings(shaft, _activeZ - d, floorStep: d - 2, maxR); // reveal z-d по связным проёмам
            }
        }

        /// <summary>Собирает дыры потолка на activeZ+1, над которыми есть сплошной этаж (иначе колонна открыта в космос).</summary>
        private List<(int, int)> CollectUpSeedOpenings(int maxFloors)
        {
            _shaft.Clear();
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
                        _shaft.Add((hx, hy));
                    }
            }
            return _shaft;
        }

        /// <summary>Есть ли сплошной уровень над (x,y) в [activeZ+2 .. activeZ+maxFloors].</summary>
        private bool HasSolidAbove(int x, int y, int maxFloors)
        {
            for (int z = _activeZ + 2; z <= _activeZ + maxFloors; z++)
                if (_map.GetTile(x, y, z).BlocksVerticalSight) return true;
            return false;
        }

        /// <summary>Собирает дыры пола z-1 под дырами пола activeZ, под которыми есть сплошной этаж.</summary>
        private List<(int, int)> CollectDownSeedOpenings(int maxR, int maxFloors)
        {
            _shaft.Clear();
            _seedSeen.Clear();
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
                                if (!_seedSeen.Add((nx, ny))) continue;
                                if (_map.GetTile(nx, ny, _activeZ - 1).BlocksVerticalSight) continue; // z-1 не дыра
                                if (!HasSolidBelow(nx, ny, maxFloors)) continue;                      // под дырой нет этажа (космос)
                                _shaft.Add((nx, ny));
                            }
                    }
            }
            return _shaft;
        }

        /// <summary>Есть ли сплошной уровень под (x,y) в [activeZ-2 .. activeZ-maxFloors].</summary>
        private bool HasSolidBelow(int x, int y, int maxFloors)
        {
            for (int z = _activeZ - 2; z >= _activeZ - maxFloors; z--)
                if (_map.GetTile(x, y, z).BlocksVerticalSight) return true;
            return false;
        }

        /// <summary>Разбивает дыры уровня lz на связные проёмы (кластеры) и раскладывает кольца reveal-кандидатов от каждого.</summary>
        private void EmitClusteredRings(List<(int, int)> shaft, int lz, int floorStep, int maxR)
        {
            _inShaft.Clear();
            foreach (var p in shaft) _inShaft.Add(p);
            _clusterVisited.Clear();
            _clusterStack.Clear();

            foreach (var start in shaft)
            {
                if (!_clusterVisited.Add(start)) continue;
                _clusterComponent.Clear();
                _clusterStack.Push(start);
                while (_clusterStack.Count > 0)
                {
                    var (cx, cy) = _clusterStack.Pop();
                    _clusterComponent.Add((cx, cy));
                    FloodVisit(cx - 1, cy, _inShaft, _clusterVisited, _clusterStack);
                    FloodVisit(cx + 1, cy, _inShaft, _clusterVisited, _clusterStack);
                    FloodVisit(cx, cy - 1, _inShaft, _clusterVisited, _clusterStack);
                    FloodVisit(cx, cy + 1, _inShaft, _clusterVisited, _clusterStack);
                }

                int clusterId = _clusterCount++;
                var centers = NextClusterCenters(clusterId);
                centers.Clear();
                foreach (var (hx, hy) in _clusterComponent) centers.Add(new Vector2(hx + 0.5f, hy + 0.5f));

                AddCandidatesAround(_clusterComponent, lz, floorStep, maxR, clusterId);
            }
        }

        private static void FloodVisit(int x, int y, HashSet<(int, int)> inShaft, HashSet<(int, int)> visited, Stack<(int, int)> stack)
        {
            var p = (x, y);
            if (inShaft.Contains(p) && visited.Add(p)) stack.Push(p);
        }

        /// <summary>Переиспользуемый список центров дыр для кластера clusterId.</summary>
        private List<Vector2> NextClusterCenters(int clusterId)
        {
            while (_clusterCenters.Count <= clusterId) _clusterCenters.Add(new List<Vector2>());
            return _clusterCenters[clusterId];
        }

        /// <summary>Добавляет reveal-кандидаты вокруг дыр кластера через shadowcast-FOV от каждой дыры на её уровне.</summary>
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

        /// <summary>(Ре)аллоцирует буфер «света от дыры» при изменении радиуса.</summary>
        private void EnsureHoleLight(int radius)
        {
            if (_holeLight != null && _holeLightRadius == radius) return;
            int size = 2 * radius + 1;
            _holeLight = new float[size, size];
            _holeLightRadius = radius;
        }

        /// <summary>Оставляет в set только точки, где на уровне lz есть вертикальная дыра (пересечение на месте, без аллокаций).</summary>
        private void FilterHolesInPlace(List<(int, int)> set, int lz)
        {
            int w = 0;
            for (int i = 0; i < set.Count; i++)
                if (!_map.GetTile(set[i].Item1, set[i].Item2, lz).BlocksVerticalSight)
                    set[w++] = set[i];
            if (w < set.Count) set.RemoveRange(w, set.Count - w);
        }

        /// <summary>Регистрирует reveal-кандидата (wx,wy,lz); при перекрытии колец выигрывает меньший Chebyshev.</summary>
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

        /// <summary>Глухая стена на reveal-тайле (для единой alpha, без градиента).</summary>
        private bool IsRevealWall(int x, int y, int z)
        {
            if (_catalog == null) return false;
            var t = _map.GetTile(x, y, z);
            if (t.StructureType == 0) return false;
            var s = _catalog.GetStructure(t.StructureType);
            return s != null && s.Category == StructureCategory.Wall;
        }

        /// <summary>Непрозрачность кольца по индексу cheb + глубина этажа (radius лишь гейтит видимость, не делит индекс).</summary>
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

        /// <summary>Alpha reveal-тайла: единая для стен, градиент по кольцу для пола/потолка.</summary>
        private float WallOrRingAlpha(bool isWall, int cheb, float radius, int floorStep)
            => isWall ? _wallRevealAlpha : RevealAlpha(cheb, radius, floorStep);

        /// <summary>Per-frame подстраивает непрозрачность reveal-тайлов под близость игрока к проёму.</summary>
        public void UpdateCeilingReveal(float px, float py)
        {
            // Short-circuit: игрок не сдвинулся и состояние валидно → результат обоих проходов байт-идентичен.
            if (_revealValid && Mathf.Abs(px - _lastPx) < RevealMoveEps && Mathf.Abs(py - _lastPy) < RevealMoveEps)
                return;
            _lastPx = px; _lastPy = py; // кэш для editor-диагностики (DescribeReveal) и short-circuit
            _revealValid = true;

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
                    SetTileAlpha(r, a); // меш: `_alpha` на TileReader-материале (MPB-preserve), без подмены материала
                }
            }
        }

        /// <summary>Считает per-frame min-дистанцию игрока до каждого кластера дыр.</summary>
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

        /// <summary>Пересобирает инстансы тайлов одного чанка.</summary>
        public void ApplyChunk(Chunk chunk)
        {
            if (_catalog == null)
            {
                Debug.LogError("[MapRenderer] TileCatalog не назначен — нечем рисовать тайлы.");
                return;
            }
            if (_map == null) return; // косвенно дёргаем _map через *Connectivity.ResolveAt — защита

            int mf = Mathf.Max(2, _revealMaxFloors);
            int delta = chunk.Z - _activeZ;
            if (delta < -mf || delta > mf) return;

            _revealValid = false; // ребилд чанка меняет состав reveal-рендеров → per-frame пройти заново

            long key = Key(chunk.ChunkX, chunk.ChunkY, chunk.Z);
            if (_chunkRoots.TryGetValue(key, out var old) && old != null)
            {
                if (_ceilingTiles.Count > 0) PruneCeilingTiles(old); // до release: убрать reveal-записи старого чанка
                ReleaseChunkTiles(old);                              // тайлы — в пул (реюз при ребилде/стриме)
                Destroy(old);                                        // пустой рут — дёшево
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

                    if (delta == 0) SpawnSpecialMarker(t.Special, wx, wy, floorBaseY, root.transform);

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
                            alpha = WallOrRingAlpha(cand.IsWall, cand.Cheb, _revealBaseRadius, cand.FloorStep); // стартовая; обновляется per-frame
                            registerCeiling = true;
                        }
                        else if (delta == 1 && _ceilingSemiTransparent && t.FloorType != 0)
                        {
                            alpha = _ceilingXrayAlpha; // рентген только для z+1 (как раньше)
                        }
                        else continue;
                    }

                    var s = t.StructureType != 0 ? _catalog.GetStructure(t.StructureType) : null;
                    bool wallHere = s != null && s.Category == StructureCategory.Wall;
                    bool hideFloor = wallHere || (s != null && s.NoVisibleFloor);

                    if (t.FloorType != 0 && !hideFloor)
                    {
                        var f = _catalog.GetFloor(t.FloorType);
                        GameObject floorGo;

                        if (f != null && f.Connection.UseConnections)
                        {
                            var (shape, steps) = FloorConnectivity.ResolveAt(_catalog, _map, f, wx, wy, chunk.Z);
                            var mesh = f.Connection.GetMesh(shape);
                            var prefab = mesh != null ? mesh : f.Prefab; // фолбэк, если меш формы не назначен
                            var rot = prefab != null ? Quaternion.Euler(0f, 90f * steps, 0f) : Quaternion.identity; // sprite-фолбэк по Y не крутим
                            floorGo = Spawn(prefab, f.Sprite, wx, wy, floorBaseY + _floorYOffset, _floorSortingOrder,
                                root.transform, "Floor", alignTop: true, planarScale: 1f, rotation: rot, alpha: alpha, deferFade: registerCeiling);
                            // Угол подаём безусловно (pool-leak иначе); +FloorCornerBaseQuarter — шов, парный EditorWindow.DrawFloorTop.
                            byte cornerMask = FloorConnectivity.ResolveCornersAt(_catalog, _map, f, wx, wy, chunk.Z);
                            var (curCorner, cornerRotate) = WallCornerCode.EncodeCorners(cornerMask);
                            byte rotate = (byte)((WallCornerCode.CompensateRotateForMesh(cornerRotate, steps)
                                + WallCornerCode.FloorCornerBaseQuarter + WallCornerCode.ShapeCornerQuarter(true, shape, curCorner)) & 3);
                            ApplyTileViewTop(floorGo, f.TopMap, shape, curCorner, rotate, f.BackingMap, null); // side=null: у пола боковин нет
                        }
                        else
                        {
                            float ps = alpha < 1f ? 1f : _floorSeamScale;
                            floorGo = Spawn(f?.Prefab, f?.Sprite, wx, wy, floorBaseY + _floorYOffset, _floorSortingOrder,
                                root.transform, "Floor", alignTop: true, planarScale: ps, rotation: Quaternion.identity, alpha: alpha, deferFade: registerCeiling);
                        }

                        if (registerCeiling) RegisterCeiling(floorGo, root, cand, wx, wy);
                    }

                    if (t.StructureType != 0)
                    {
                        bool openNow = s != null && s.Openable && t.Open;
                        GameObject structGo;

                        if (s != null && !openNow && s.Category == StructureCategory.Wall && s.Connection.UseConnections)
                        {
                            var (shape, steps) = WallConnectivity.ResolveAt(_catalog, _map, s, wx, wy, chunk.Z);
                            var mesh = s.Connection.GetMesh(shape);
                            var prefab = mesh != null ? mesh : s.Prefab; // фолбэк, если меш формы не назначен
                            var rot = prefab != null ? Quaternion.Euler(0f, 90f * steps, 0f) : Quaternion.identity; // sprite-фолбэк по Y не крутим
                            structGo = Spawn(prefab, s.Sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder,
                                root.transform, "Structure", alignTop: false, planarScale: 1f, rotation: rot, alpha: alpha, deferFade: registerCeiling);
                            // Меш повёрнут на 90°·steps, UV мешевые (TEXCOORD0) → компенсируем _rotate на -steps (double-rotate trap).
                            byte cornerMask = WallConnectivity.ResolveCornersAt(_catalog, _map, s, wx, wy, chunk.Z);
                            var (curCorner, cornerRotate) = WallCornerCode.EncodeCorners(cornerMask);
                            byte rotate = (byte)((WallCornerCode.CompensateRotateForMesh(cornerRotate, steps)
                                + WallCornerCode.ShapeCornerQuarter(false, shape, curCorner)) & 3);
                            ApplyTileViewTop(structGo, s.TopMap, shape, curCorner, rotate, s.BackingMap, s.SideMap);
                        }
                        else
                        {
                            var prefab = openNow ? s.OpenPrefab : s?.Prefab;
                            var sprite = openNow ? s.OpenSprite : s?.Sprite;
                            var rot = (s != null && s.Openable) ? DoorRotation(wx, wy, chunk.Z) : Quaternion.identity;
                            structGo = Spawn(prefab, sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder,
                                root.transform, "Structure", alignTop: false, planarScale: 1f, rotation: rot, alpha: alpha, deferFade: registerCeiling);
                        }

                        if (registerCeiling) RegisterCeiling(structGo, root, cand, wx, wy);
                    }
                }
            }
        }

        /// <summary>Создаёт инстанс тайла (префаб/sprite-фолбэк), выравнивает по грани и применяет fade.</summary>
        private GameObject Spawn(GameObject prefab, Sprite sprite, int wx, int wy, float y, int sortingOrder,
            Transform parent, string label, bool alignTop, float planarScale, Quaternion rotation, float alpha = 1f, bool deferFade = false)
        {
            GameObject go;
            if (prefab != null)
            {
                go = AcquirePrefab(prefab, parent);
            }
            else if (sprite != null)
            {
                go = AcquireSprite(label, parent);
                var sr = go.GetComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = sortingOrder;
                var col = sr.color; col.a = deferFade ? 1f : alpha; sr.color = col; // color уже сброшен к pristine в ResetToPristine
            }
            else
            {
                return null; // ни префаба, ни спрайта
            }

            go.transform.rotation = rotation;

            // localScale уже pristine (ResetToPristine на реюзе) — planarScale не накапливается между реюзами.
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

        /// <summary>Выдаёт prefab-инстанс из пула (со сбросом состояния) или создаёт новый.</summary>
        private GameObject AcquirePrefab(GameObject prefab, Transform parent)
        {
            if (_tilePool.TryGetValue(prefab, out var stack))
            {
                var go = PopLive(stack); // null-safe: пропускаем уничтоженные (отложенный Destroy) ссылки в пуле
                if (go != null)
                {
                    go.transform.SetParent(parent, false);
                    var pt = go.GetComponent<PooledTile>();
                    if (pt != null) pt.ResetToPristine(); // сброс материала/MPB/shadow/scale/color ДО новой заливки
                    go.SetActive(true);
                    return go;
                }
            }
            var inst = Instantiate(prefab, parent);
            inst.AddComponent<PooledTile>().Init(prefab, isSpriteFallback: false); // снимок pristine до модификаций
            return inst;
        }

        /// <summary>Выдаёт sprite-fallback GO из своего стека (со сбросом) или создаёт новый.</summary>
        private GameObject AcquireSprite(string label, Transform parent)
        {
            var go = PopLive(_spritePool); // null-safe pop
            if (go != null)
            {
                go.name = label;
                go.transform.SetParent(parent, false);
                var pt = go.GetComponent<PooledTile>();
                if (pt != null) pt.ResetToPristine();
                go.SetActive(true);
                return go;
            }
            var inst = new GameObject(label);
            inst.transform.SetParent(parent, false);
            inst.AddComponent<SpriteRenderer>();
            inst.AddComponent<PooledTile>().Init(null, isSpriteFallback: true);
            return inst;
        }

        /// <summary>Достаёт живой GO из стека пула, пропуская уничтоженные ссылки.</summary>
        private GameObject PopLive(Stack<GameObject> stack)
        {
            while (stack.Count > 0)
            {
                var go = stack.Pop();
                if (go == null) continue; // Unity-переопределённый == : уничтоженный GO == null → выкидываем
                var pt = go.GetComponent<PooledTile>();
                if (pt != null) pt.InPool = false;
                return go;
            }
            return null;
        }

        /// <summary>Возвращает все тайл-GO чанка в пул. Вызывать после PruneCeilingTiles(root).</summary>
        private void ReleaseChunkTiles(GameObject root)
        {
            var tr = root.transform;
            for (int i = tr.childCount - 1; i >= 0; i--) // назад: reparent не сдвигает ещё не обойдённые индексы
                Release(tr.GetChild(i).gameObject);
        }

        /// <summary>Освобождает один тайл-GO в пул: деактивирует, репарентит и кладёт в стек по источнику.</summary>
        private void Release(GameObject go)
        {
            var pt = go.GetComponent<PooledTile>();
            if (pt == null) { Destroy(go); return; } // не пуловый (страховка) — просто уничтожить

            // Double-release guard: флаг на PooledTile, не HashSet<GameObject> (хэш Unity-объекта «плывёт» после Destroy).
            if (pt.InPool)
            {
                Debug.LogWarning($"[MapRenderer] double-release тайла '{go.name}' (source={(pt.Source != null ? pt.Source.name : "sprite")}) — второй push пропущен (корень double-release)");
                return;
            }

            Stack<GameObject> stack;
            if (pt.IsSpriteFallback) stack = _spritePool;
            else if (!_tilePool.TryGetValue(pt.Source, out stack)) { stack = new Stack<GameObject>(); _tilePool[pt.Source] = stack; }

            if (stack.Count >= MaxPooledPerKey) { Destroy(go); return; } // кап — лишок уничтожаем (InPool не ставили — трекинг чист)

            EnsurePoolRoot();
            pt.InPool = true;
            go.SetActive(false);
            go.transform.SetParent(_poolRoot, false);
            stack.Push(go);
        }

        private void EnsurePoolRoot()
        {
            if (_poolRoot != null) return;
            var go = new GameObject("~TilePool");
            go.transform.SetParent(transform, false);
            go.SetActive(false); // неактивный контейнер — released-тайлы не рисуются/не тикают
            _poolRoot = go.transform;
        }

        /// <summary>Регистрирует рендеры верхнего тайла для динамического reveal.</summary>
        private void RegisterCeiling(GameObject go, GameObject root, in CeilingCandidate cand, int wx, int wy)
        {
            if (go == null) return;
            bool opaque = cand.IsWall && _wallRevealAlpha >= 1f; // непрозрачная стена → alpha не трогаем (дефолт материала 1)
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
                        BaseColor = baseC,
                        Wx = wx, Wy = wy, Lz = cand.Lz, FloorStep = cand.FloorStep, ClusterId = cand.ClusterId, IsWall = cand.IsWall, Opaque = opaque
                    });
                    if (!opaque) { var col = baseC; col.a = startAlpha; sr.color = col; } // opaque — alpha остаётся 1
                    continue;
                }

                _ceilingTiles.Add(new CeilingReveal
                {
                    Renderer = r, Root = root, IsSprite = false, Cheb = cand.Cheb,
                    BaseColor = Color.white, // для меша не используется (alpha через _alpha материала)
                    Wx = wx, Wy = wy, Lz = cand.Lz, FloorStep = cand.FloorStep, ClusterId = cand.ClusterId, IsWall = cand.IsWall, Opaque = opaque
                });

                if (opaque) continue; // непрозрачная стена: только видимость (гейт близости), материал/alpha не трогаем

                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // полупрозрачный верх не льёт тень
                SetTileAlpha(r, startAlpha);
            }
        }

        /// <summary>Убирает из reveal-реестра записи чанка root (вызывать до Destroy(root)).</summary>
        private void PruneCeilingTiles(GameObject root)
        {
            for (int i = _ceilingTiles.Count - 1; i >= 0; i--)
                if (_ceilingTiles[i].Root == root) _ceilingTiles.RemoveAt(i);
        }

        /// <summary>Гасит меш-рендеры до alpha через `_alpha` (xray/полупрозрачный тайл вне reveal-реестра).</summary>
        private void ApplyFade(Renderer[] renderers, float alpha)
        {
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue; // спрайтам alpha уже задана через color
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // полупрозрачный верх не льёт тень
                SetTileAlpha(r, alpha);
            }
        }

        /// <summary>Задаёт reveal `_alpha` тайла через MPB, сохраняя прочие свойства блока.</summary>
        private void SetTileAlpha(Renderer r, float alpha)
        {
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            r.GetPropertyBlock(_mpb); // сохранить _cur/_cur_corner/_TopMap/_DownTex
            _mpb.SetFloat(AlphaId, alpha);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Диагностика (CornerProbe): текстовое описание углового слоя пола (x,y,z), как его считает floor-ветка ApplyChunk.</summary>
        public string DescribeFloorCorner(int x, int y, int z)
        {
            if (_map == null || _catalog == null) return "CornerProbe: _map/_catalog == null";
            var t = _map.GetTile(x, y, z);
            if (t.FloorType == 0) return $"({x},{y},z{z}): пола нет (FloorType=0)";
            var f = _catalog.GetFloor(t.FloorType);
            if (f == null) return $"({x},{y},z{z}): FloorType={t.FloorType}, нет FloorDefinition в каталоге";
            if (f.Connection == null || !f.Connection.UseConnections)
                return $"({x},{y},z{z}): пол '{f.DisplayName}' БЕЗ autotiling (UseConnections=false) → угловой слой не подаётся";

            var (shape, steps) = FloorConnectivity.ResolveAt(_catalog, _map, f, x, y, z);
            byte mask = FloorConnectivity.ResolveCornersAt(_catalog, _map, f, x, y, z);
            var (curCorner, cornerRotate) = WallCornerCode.EncodeCorners(mask);
            byte rotate = (byte)((WallCornerCode.CompensateRotateForMesh(cornerRotate, steps)
                + WallCornerCode.FloorCornerBaseQuarter + WallCornerCode.ShapeCornerQuarter(true, shape, curCorner)) & 3);
            return $"({x},{y},z{z}) '{f.DisplayName}': shape={shape} steps={steps}   mask={mask} "
                 + $"[NW={(mask & 1) != 0} NE={(mask & 2) != 0} SE={(mask & 4) != 0} SW={(mask & 8) != 0}]   "
                 + $"_cur_corner={curCorner} _rotate={rotate}";
        }


        /// <summary>Заливает материал верхней грани тайла (форма/углы/подложка) через MPB, изолируя боковину стены.</summary>
        private void ApplyTileViewTop(GameObject go, Texture2D topMap, WallShape shape, byte curCorner, byte rotate,
            Texture2D backing, Texture2D side)
        {
            if (go == null) return;
            _wallMpb ??= new MaterialPropertyBlock();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r is SpriteRenderer) continue; // спрайт-фолбэк — другой материал

                var mat = r.sharedMaterial;
                if (mat != null && mat.name.Contains("TileWall")) // TileWall-skip: боковина изолирована от формы верха
                {
                    if (side == null) continue;
                    _sideMpb ??= new MaterialPropertyBlock();
                    _sideMpb.Clear();
                    r.GetPropertyBlock(_sideMpb);
                    _sideMpb.SetTexture(TopMapId, side);
                    r.SetPropertyBlock(_sideMpb);
                    continue;
                }

                _wallMpb.Clear();
                r.GetPropertyBlock(_wallMpb); // сохранить уже выставленный блок, иначе пусто
                if (topMap != null)
                    _wallMpb.SetTexture(TopMapId, topMap);
                if (backing != null)
                    _wallMpb.SetTexture(DownTexId, backing); // инертно до появления _DownTex в шейдере (подложка чёрная)
                // uint в CBUFFER шейдера → SetInteger (SetFloat залил бы биты float, прочитанные как мусорный uint).
                // Всегда безусловно — иначе несписанный из пула MPB подхватит дефолт материала (чужие углы, pool-leak).
                _wallMpb.SetInteger(CurId, WallCornerCode.CurFromShape(shape));
                _wallMpb.SetInteger(CurCornerId, curCorner);
                _wallMpb.SetInteger(RotateId, rotate);
                r.SetPropertyBlock(_wallMpb);
            }
        }

        /// <summary>Спавнит оверлей-маркер спец-тайла (лестница/спавн) в жизненном цикле чанка.</summary>
        private void SpawnSpecialMarker(TileSpecial special, int wx, int wy, float floorBaseY, Transform parent)
        {
            Sprite marker = special switch
            {
                TileSpecial.StairUp => _stairUpMarker,
                TileSpecial.StairDown => _stairDownMarker,
                TileSpecial.Spawn => _spawnMarker,
                _ => null // None/Lift — без маркера
            };
            if (marker == null) return;

            Spawn(null, marker, wx, wy, floorBaseY + _markerYOffset, _markerSortingOrder,
                parent, "SpecialMarker", alignTop: true, planarScale: 1f, rotation: Quaternion.identity);
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

        /// <summary>Снести рендер одного чанка (стриминговая выгрузка ChunkUnload).</summary>
        public void RemoveChunk(int cx, int cy, int z)
        {
            long key = Key(cx, cy, z);
            if (!_chunkRoots.TryGetValue(key, out var root)) return;

            if (root != null)
            {
                if (_ceilingTiles.Count > 0) PruneCeilingTiles(root); // до release: без «висячих» reveal-записей
                ReleaseChunkTiles(root);                              // тайлы — в пул
                Destroy(root);
            }
            _chunkRoots.Remove(key);
            _revealValid = false; // состав reveal-рендеров изменился
        }

        private void ClearAll()
        {
            // Чистим реестр reveal ДО release тайлов — O(1) вместо per-root PruneCeilingTiles (O(n²)).
            _ceilingTiles.Clear();
            foreach (var root in _chunkRoots.Values)
                if (root != null) { ReleaseChunkTiles(root); Destroy(root); } // тайлы в пул (реюз при смене Z), рут — снести
            _chunkRoots.Clear();
            _revealValid = false;
        }

        private static long Key(int cx, int cy, int z) // та же упаковка ключа, что в GridMap
        {
            return ((long)(cx & 0x1FFFFF))
                 | ((long)(cy & 0x1FFFFF) << 21)
                 | ((long)(z & 0x1FFFFF) << 42);
        }

#if UNITY_EDITOR
        // Editor-only диагностика reveal (см. CeilingRevealProbe): в билд не входит, только читает состояние.

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
                            + $"Cheb={rec.Cheb}, FloorStep={rec.FloorStep}, ClusterId={rec.ClusterId}");
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

        /// <summary>Min-дистанция от последней player-позиции до дыр кластера (editor-пересчёт).</summary>
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

        /// <summary>Дописывает ASCII мини-карту по z-уровням вокруг probe/player.</summary>
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

            int mf = Mathf.Max(2, _revealMaxFloors);
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

    /// <summary>Метка тайл-GO пула MapRenderer: источник для release-маршрутизации + pristine-снимок для полного сброса при реюзе.</summary>
    internal sealed class PooledTile : MonoBehaviour
    {
        public GameObject Source { get; private set; }     // ключ пула (null для sprite-fallback)
        public bool IsSpriteFallback { get; private set; }
        public bool InPool;                                // integrity-флаг: тайл сейчас в пуле (double-release guard)

        private Renderer[] _renderers;
        private Material[][] _pristineMats;
        private UnityEngine.Rendering.ShadowCastingMode[] _pristineShadow;
        private Color[] _pristineColor;                    // осмыслен для SpriteRenderer (mesh — не используется)
        private bool[] _pristineEnabled;                   // Renderer.enabled: reveal гасит его per-frame → течёт через пул
        private Vector3 _pristineScale;
        private MaterialPropertyBlock _clearBlock;

        /// <summary>Снять «чистый» снимок сразу после создания GO (ДО модификаций Spawn/фейда/TileView).</summary>
        public void Init(GameObject source, bool isSpriteFallback)
        {
            Source = source;
            IsSpriteFallback = isSpriteFallback;
            _pristineScale = transform.localScale;
            _renderers = GetComponentsInChildren<Renderer>(true);
            int n = _renderers.Length;
            _pristineMats = new Material[n][];
            _pristineShadow = new UnityEngine.Rendering.ShadowCastingMode[n];
            _pristineColor = new Color[n];
            _pristineEnabled = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var r = _renderers[i];
                _pristineMats[i] = r.sharedMaterials;      // shared (не материал-инстанс) → без утечек
                _pristineShadow[i] = r.shadowCastingMode;
                _pristineColor[i] = r is SpriteRenderer sr ? sr.color : Color.white;
                _pristineEnabled[i] = r.enabled;           // снимок ДО модификаций (reveal позже гасит per-frame)
            }
        }

        /// <summary>Полный сброс к «чистому» состоянию при выдаче из пула (перед новой заливкой Spawn/ApplyChunk).</summary>
        public void ResetToPristine()
        {
            transform.localScale = _pristineScale;
            _clearBlock ??= new MaterialPropertyBlock();
            _clearBlock.Clear();
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.enabled = _pristineEnabled[i];           // undo reveal enabled=false (иначе реюзнутый тайл невидим НАВСЕГДА)
                r.sharedMaterials = _pristineMats[i];      // реставрация материала (reveal больше не свапает; защитно)
                r.shadowCastingMode = _pristineShadow[i];  // undo reveal Off
                r.SetPropertyBlock(_clearBlock);           // сброс MPB (_cur/_cur_corner/_rotate/_TopMap/_alpha) → дефолты материала (_alpha=1)
                if (r is SpriteRenderer sr) sr.color = _pristineColor[i];
            }
        }
    }
}
