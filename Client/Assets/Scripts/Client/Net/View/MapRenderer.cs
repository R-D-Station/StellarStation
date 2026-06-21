using System.Collections.Generic;
using UnityEngine;
using Shared.World;

namespace Client.Net.View
{
    /// <summary>
    /// Рендер карты в Unity. Держит GridMap и рисует каждый чанк отдельным
    /// мешем (один меш на чанк, пол+стены — решение этапа 1).
    ///
    /// Источник карты развязан, как транспорт: сейчас грузим .smap локально
    /// (тест без сервера), позже SetMap/ApplyChunk будут вызываться из сетевого
    /// кода при chunk-streaming — рендер не изменится, он работает с GridMap.
    ///
    /// Этап 1: рисуем ВСЕ чанки карты (нет PVS, нет выбора этажа в рендере).
    /// Видимость по этажам/FOV — этап 3.
    /// </summary>
    public class MapRenderer : MonoBehaviour
    {
        [Header("Atlas (договорённость B: FloorType/WallType -> ячейка атласа)")]
        [Tooltip("Материал с текстурой-атласом тайлов. Unlit/спрайт-шейдер.")]
        [SerializeField] private Material _atlasMaterial;

        [Tooltip("Сетка атласа: столбцов x строк. Тип N -> ячейка (N-1).")]
        [SerializeField] private Vector2Int _atlasGrid = new Vector2Int(4, 4);

        [Tooltip("Размер тайла в юнитах Unity. 1 = 1 тайл на юнит.")]
        [SerializeField] private float _tileSize = 1f;

        [Tooltip("Высота этажа по Unity-Y. Для плоского теста = 0.")]
        [SerializeField] private float _floorHeight = 0f;

        [Header("Локальный тест (без сервера)")]
        [Tooltip("Абсолютный путь к .smap для загрузки при старте. Пусто = не грузить.")]
        [SerializeField] private string _testMapPath = "";

        private GridMap _map;
        private ChunkMeshBuilder _builder;

        // По одному объекту-рендеру на чанк, ключ — тот же упакованный (cx,cy,z).
        private readonly Dictionary<long, ChunkRenderObject> _renderObjects = new();

        private sealed class ChunkRenderObject
        {
            public GameObject Go;
            public MeshFilter Filter;
            public Mesh Mesh;
        }

        private void Awake()
        {
            _builder = new ChunkMeshBuilder(_atlasGrid.x, _atlasGrid.y, _tileSize);
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(_testMapPath))
                LoadLocal(_testMapPath);
        }

        /// <summary>Загрузить карту из .smap (локальный тест). Тот же сериализатор, что у сервера.</summary>
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

        /// <summary>
        /// Заменить всю карту и перестроить рендер. Вызывается из локальной
        /// загрузки или (позже) из сетевого кода при первичной синхронизации.
        /// </summary>
        public void SetMap(GridMap map)
        {
            ClearAll();
            _map = map;
            if (_map == null) return;

            foreach (var chunk in _map.Chunks)
                ApplyChunk(chunk);
        }

        /// <summary>
        /// Построить/обновить рендер одного чанка. Точка входа для chunk-streaming:
        /// сетевой код будет звать это при получении чанка от сервера.
        /// </summary>
        public void ApplyChunk(Chunk chunk)
        {
            long key = Key(chunk.ChunkX, chunk.ChunkY, chunk.Z);

            if (!_renderObjects.TryGetValue(key, out var ro))
            {
                var go = new GameObject($"Chunk_{chunk.ChunkX}_{chunk.ChunkY}_z{chunk.Z}");
                go.transform.SetParent(transform, false);

                // Позиция чанка в мире: угол чанка в тайлах * tileSize, высота по Z.
                go.transform.localPosition = new Vector3(
                    chunk.ChunkX * Chunk.Size * _tileSize,
                    chunk.Z * _floorHeight,
                    chunk.ChunkY * Chunk.Size * _tileSize);

                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _atlasMaterial;

                ro = new ChunkRenderObject { Go = go, Filter = filter };
                _renderObjects[key] = ro;
            }

            ro.Mesh = _builder.Build(chunk, ro.Mesh);
            ro.Filter.sharedMesh = ro.Mesh;
        }

        private void ClearAll()
        {
            foreach (var ro in _renderObjects.Values)
            {
                if (ro.Mesh != null) Destroy(ro.Mesh);
                if (ro.Go != null) Destroy(ro.Go);
            }
            _renderObjects.Clear();
        }

        // Тот же ключ, что в GridMap — чтобы рендер-объекты адресовались одинаково.
        private static long Key(int cx, int cy, int z)
        {
            return ((long)(cx & 0x1FFFFF))
                 | ((long)(cy & 0x1FFFFF) << 21)
                 | ((long)(z & 0x1FFFFF) << 42);
        }
    }
}