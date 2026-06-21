using System.Collections.Generic;
using UnityEngine;
using Shared.World;
using Client.Map;
using Client.Config;

namespace Client.Net.View
{
    /// <summary>Рисует GridMap префабами из TileCatalog в 2.5D-раскладке (этажи по Unity-Y).</summary>
    public class MapRenderer : MonoBehaviour
    {
        [Header("Каталог тайлов (id → префаб/спрайт)")]
        [SerializeField] private TileCatalog _catalog;

        [Header("Этаж/высота (2.5D: этажи стоят по Unity-Y)")]
        [Tooltip("Какой Z-этаж рисуем. Этап 3 добавит соседние слои.")]
        [SerializeField] private int _activeZ = 0;
        [Tooltip("Доп. сдвиг пола по Y над уровнем этажа (обычно 0).")]
        [SerializeField] private float _floorYOffset = 0f;
        [Tooltip("Доп. сдвиг стены по Y над полом — против z-fighting у плоских квадов.")]
        [SerializeField] private float _wallYOffset = 0.001f;
        [SerializeField] private int _floorSortingOrder = 0;
        [SerializeField] private int _wallSortingOrder = 10;

        [Tooltip("Лёгкое расширение тайлов пола в плоскости (1.0 = выкл), чтобы соседние " +
                 "префабы перекрывались и не показывали шов при движении камеры. " +
                 "Если на полу появится мерцание (z-fighting) — уменьшай к 1.0.")]
        [SerializeField] private float _floorSeamScale = 1.02f;

        [Header("Тестовая карта (для отладки без сервера)")]
        [Tooltip("Относительный путь к .smap для загрузки при старте. Пусто = не грузить.")]
        [SerializeField] private string _testMapPath = "";

        private GridMap _map;

        // Корень-объект на каждый чанк; ключ тот же, что в GridMap.
        private readonly Dictionary<long, GameObject> _chunkRoots = new();

        private void Start()
        {
            if (!string.IsNullOrEmpty(_testMapPath))
                LoadLocal(_testMapPath);
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

        /// <summary>Полностью заменить карту. Рисуем только чанки активного этажа (_activeZ).</summary>
        public void SetMap(GridMap map)
        {
            ClearAll();
            _map = map;
            if (_map == null) return;

            foreach (var chunk in _map.Chunks)
                if (chunk.Z == _activeZ)
                    ApplyChunk(chunk);
        }

        /// <summary>Сменить активный этаж и перерисовать (заготовка под этаж-переходы, этап 3).</summary>
        public void SetActiveZ(int z)
        {
            if (_activeZ == z) return;
            _activeZ = z;
            if (_map != null) SetMap(_map);
        }

        /// <summary>Перерисовать чанк тайла после рантайм-изменения (TileUpdate).</summary>
        public void RefreshTileAt(int x, int y, int z)
        {
            if (_map == null || z != _activeZ) return;
            var chunk = _map.GetChunk(FloorDiv(x, Chunk.Size), FloorDiv(y, Chunk.Size), z);
            if (chunk != null) ApplyChunk(chunk);
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>Поворот двери по ориентации стены: стена север-юг → поворот на 90°.</summary>
        private Quaternion DoorRotation(int x, int y, int z)
        {
            if (_map == null) return Quaternion.identity;
            bool wallEW = _map.GetTile(x - 1, y, z).WallType != 0 || _map.GetTile(x + 1, y, z).WallType != 0;
            bool wallNS = _map.GetTile(x, y - 1, z).WallType != 0 || _map.GetTile(x, y + 1, z).WallType != 0;
            if (!wallNS && wallEW) return Quaternion.Euler(0f, 90f, 0f);
            return Quaternion.identity;
        }

        /// <summary>Пересобрать инстансы тайлов одного чанка.</summary>
        public void ApplyChunk(Chunk chunk)
        {
            if (_catalog == null)
            {
                Debug.LogError("[MapRenderer] TileCatalog не назначен — нечем рисовать тайлы.");
                return;
            }

            if (chunk.Z != _activeZ)
                return;

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
                    int wx = baseX + lx;
                    int wy = baseY + ly;

                    if (t.FloorType != 0)
                    {
                        var f = _catalog.GetFloor(t.FloorType);
                        Spawn(f?.Prefab, f?.Sprite, wx, wy, floorBaseY + _floorYOffset, _floorSortingOrder, root.transform, "Floor", alignTop: true, planarScale: _floorSeamScale, rotation: Quaternion.identity);
                    }

                    if (t.WallType != 0)
                    {
                        var w = _catalog.GetWall(t.WallType);
                        Spawn(w?.Prefab, w?.Sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder, root.transform, "Wall", alignTop: false, planarScale: 1f, rotation: Quaternion.identity);
                    }

                    if (t.DoorType != 0)
                    {
                        var d = _catalog.GetDoor(t.DoorType);
                        var prefab = t.DoorOpen ? d?.OpenPrefab : d?.ClosedPrefab;
                        var sprite = t.DoorOpen ? d?.OpenSprite : d?.ClosedSprite;
                        Spawn(prefab, sprite, wx, wy, floorBaseY + _wallYOffset, _wallSortingOrder, root.transform, "Door", alignTop: false, planarScale: 1f, rotation: DoorRotation(wx, wy, chunk.Z));
                    }
                }
            }
        }

        /// <summary>Создать инстанс тайла (префаб или fallback-спрайт) в центре тайла и выровнять по грани.</summary>
        private void Spawn(GameObject prefab, Sprite sprite, int wx, int wy, float y, int sortingOrder, Transform parent, string label, bool alignTop, float planarScale, Quaternion rotation)
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
            }
            else
            {
                return; // ни префаба, ни спрайта
            }

            go.transform.rotation = rotation;

            // Чуть расширяем тайл в плоскости XZ против шва на стыках.
            if (planarScale != 1f)
            {
                Vector3 s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x * planarScale, s.y, s.z * planarScale);
            }

            // +0.5 — центр тайла (целое (wx,wy) — угол), чтобы сетка совпала с коллизией.
            go.transform.position = new Vector3(wx + 0.5f, y, wy + 0.5f);
            AlignEdgeTo(go, y, alignTop);
        }

        /// <summary>Сдвинуть объект по Y, чтобы его верхняя/нижняя грань легла на targetY.</summary>
        private static void AlignEdgeTo(GameObject go, float targetY, bool alignTop)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
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
