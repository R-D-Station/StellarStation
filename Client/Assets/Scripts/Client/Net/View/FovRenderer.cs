using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Shared.World;
using Shared.Simulation;

namespace Client.Net.View
{
    /// <summary>Туман войны: строит меш полигона видимости и рисует его в маску-RenderTexture на GPU.</summary>
    public class FovRenderer : MonoBehaviour
    {
        [Tooltip("Материал тумана (Hidden/Station/FogOfWar). Этот же — в Full Screen Pass Renderer Feature.")]
        [SerializeField] private Material _fogMaterial;
        [Tooltip("Материал маски (Hidden/Station/FovMask) — им рисуется меш полигона в RenderTexture.")]
        [SerializeField] private Material _maskMaterial;
        [Tooltip("Радиус обзора в тайлах.")]
        [SerializeField] private int _radius = 20;
        [Tooltip("Яркость невидимых зон: 0 — чёрные, 1 — без затемнения.")]
        [Range(0f, 1f)] [SerializeField] private float _minBrightness = 0.08f;
        [Tooltip("Доп. мягкость края (GPU 3x3 в шейдере тумана), в текселях маски.")]
        [SerializeField] private float _edgeSoftness = 1f;
        [Tooltip("Размер маски-RenderTexture (px). 512 обычно достаточно.")]
        [SerializeField] private int _rtSize = 512;

        private GridMap _map;
        private RenderTexture _rt;
        private Mesh _mesh;
        private CommandBuffer _cmd;
        private readonly List<Vector3> _verts = new();
        private readonly List<Color> _cols = new();
        private readonly List<int> _tris = new();
        private bool _dirty = true;
        private int _lastTileX = int.MinValue, _lastTileY = int.MinValue, _lastZ = int.MinValue;

        private static readonly int FovTexId = Shader.PropertyToID("_FovTex");
        private static readonly int FovParamsId = Shader.PropertyToID("_FovParams");
        private static readonly int MinBrightId = Shader.PropertyToID("_FovMinBrightness");
        private static readonly int FovTexelId = Shader.PropertyToID("_FovTexel");
        private static readonly int FovSoftId = Shader.PropertyToID("_FovSoft");

        public void SetMap(GridMap map) { _map = map; _dirty = true; }

        /// <summary>Заставить пересчитать FOV (напр. дверь открылась/закрылась).</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>Обновить туман. worldX = серверный X, worldZ = серверный Y (глубина).</summary>
        public void UpdateFov(float worldX, float worldZ, int z)
        {
            if (_map == null || _fogMaterial == null || _maskMaterial == null) return;

            int tileX = Mathf.FloorToInt(worldX);
            int tileY = Mathf.FloorToInt(worldZ);
            if (!_dirty && tileX == _lastTileX && tileY == _lastTileY && z == _lastZ) return;
            _dirty = false;
            _lastTileX = tileX; _lastTileY = tileY; _lastZ = z;

            EnsureResources();

            float winMinX = worldX - _radius;
            float winMinY = worldZ - _radius;
            float winSize = 2f * _radius;

            var poly = Visibility.ComputePolygon(_map, worldX, worldZ, z, _radius);
            BuildMesh(poly, worldX, worldZ);

            // Рисуем меш в маску: ортопроекция окна мира в клип.
            _cmd.Clear();
            _cmd.SetRenderTarget(_rt);
            _cmd.ClearRenderTarget(true, true, Color.black);
            Matrix4x4 proj = Matrix4x4.Ortho(winMinX, winMinX + winSize, winMinY, winMinY + winSize, -1f, 1f);
            _cmd.SetViewProjectionMatrices(Matrix4x4.identity, proj);
            _cmd.DrawMesh(_mesh, Matrix4x4.identity, _maskMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(_cmd);

            _fogMaterial.SetTexture(FovTexId, _rt);
            _fogMaterial.SetVector(FovParamsId, new Vector4(winMinX, winMinY, 1f / winSize, 1f / winSize));
            _fogMaterial.SetFloat(MinBrightId, _minBrightness);
            _fogMaterial.SetFloat(FovTexelId, 1f / _rtSize);
            _fogMaterial.SetFloat(FovSoftId, _edgeSoftness);
        }

        private void BuildMesh(List<Visibility.Vec2> poly, float ox, float oz)
        {
            _verts.Clear();
            _cols.Clear();
            _tris.Clear();

            _verts.Add(new Vector3(ox, oz, 0f)); // центр — игрок
            _cols.Add(Color.white);

            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i];
                float dx = p.X - ox, dy = p.Y - oz;
                float l = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / (_radius + 1));
                _verts.Add(new Vector3(p.X, p.Y, 0f));
                _cols.Add(new Color(l, l, l, 1f));
            }

            for (int i = 0; i < n; i++)
            {
                _tris.Add(0);
                _tris.Add(i + 1);
                _tris.Add((i + 1) % n + 1);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_cols);
            _mesh.SetTriangles(_tris, 0);
        }

        private void EnsureResources()
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(_rtSize, _rtSize, 0, RenderTextureFormat.R8)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "FovMask"
                };
                _rt.Create();
            }
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "FovPolygon" };
                _mesh.MarkDynamic();
            }
            _cmd ??= new CommandBuffer { name = "FOV Mask" };
        }

        private void OnDestroy()
        {
            if (_rt != null) _rt.Release();
            _cmd?.Release();
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
