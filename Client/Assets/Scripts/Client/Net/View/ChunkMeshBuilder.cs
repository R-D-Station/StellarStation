using System.Collections.Generic;
using UnityEngine;
using Shared.World;

namespace Client.Net.View
{
    /// <summary>
    /// Строит один Mesh для чанка: по квадру на каждый видимый тайл.
    /// Пол (FloorType != 0) и стены (WallType != 0) — в ОДНОМ меше (решение
    /// этапа 1: стены плоские). UV берутся из атласа арифметикой по номеру типа
    /// (договорённость B): тип N -> ячейка N в сетке атласа AtlasCols x AtlasRows.
    ///
    /// Координаты — серверные: тайл (x,y) на этаже z кладётся в Unity как
    /// плоскость XZ_unity, где Unity-X = x, Unity-Z(глубина) = y. Высота (Unity-Y)
    /// = z * floorHeight задаётся объектом-родителем чанка, здесь меш строится
    /// в локальных координатах от угла чанка, плоско по Y=0.
    ///
    /// Чистый класс (не MonoBehaviour): меш-логику можно гонять без сцены.
    /// </summary>
    public sealed class ChunkMeshBuilder
    {
        private readonly int _atlasCols;
        private readonly int _atlasRows;
        private readonly float _tileSize;

        // Небольшой подъём стен над полом по Y, чтобы при равных координатах
        // стена не z-fighting'ила с полом. Не «высота стены» — просто эпсилон.
        private const float WallYOffset = 0.001f;

        // Переиспользуемые буферы — чтобы не аллоцировать список на каждый чанк.
        private readonly List<Vector3> _verts = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _tris = new();

        public ChunkMeshBuilder(int atlasCols, int atlasRows, float tileSize = 1f)
        {
            _atlasCols = Mathf.Max(1, atlasCols);
            _atlasRows = Mathf.Max(1, atlasRows);
            _tileSize = tileSize;
        }

        /// <summary>
        /// Построить (или перестроить) меш чанка. Передавай существующий Mesh,
        /// чтобы не плодить аллокации при перестройке; null — создаст новый.
        /// </summary>
        public Mesh Build(Chunk chunk, Mesh reuse = null)
        {
            _verts.Clear();
            _uvs.Clear();
            _tris.Clear();

            for (int ly = 0; ly < Chunk.Size; ly++)
            {
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    Tile t = chunk[lx, ly];

                    // Пол.
                    if (t.FloorType != 0)
                        AddQuad(lx, ly, 0f, t.FloorType);

                    // Стена поверх пола (тот же квад, чуть выше по Y).
                    if (t.WallType != 0)
                        AddQuad(lx, ly, WallYOffset, t.WallType);
                }
            }

            var mesh = reuse ?? new Mesh { name = "ChunkMesh" };
            mesh.Clear();

            // Чанк 16x16, максимум 2 квада на тайл -> 16*16*2*4 = 2048 вершин.
            // Влезает в 16-битный индекс (<65535), UInt16 по умолчанию — ок.
            mesh.SetVertices(_verts);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
            // Нормали не нужны для unlit-спрайт-материала; если будет освещение —
            // добавить mesh.RecalculateNormals().
            return mesh;
        }

        /// <summary>
        /// Квад в плоскости XZ Unity на высоте yOffset. Локальная позиция тайла:
        /// Unity-X = lx, Unity-Z(глубина) = ly. Это согласовано с маппингом
        /// сущностей (NetEntityView: Vector3(x, _, y)) — пол и сущности в одной
        /// системе, сортировка совпадает.
        /// </summary>
        private void AddQuad(int lx, int ly, float yOffset, byte type)
        {
            float x0 = lx * _tileSize;
            float z0 = ly * _tileSize;
            float x1 = x0 + _tileSize;
            float z1 = z0 + _tileSize;

            int baseIndex = _verts.Count;

            // 4 угла квада, плоско по Y.
            _verts.Add(new Vector3(x0, yOffset, z0));
            _verts.Add(new Vector3(x1, yOffset, z0));
            _verts.Add(new Vector3(x1, yOffset, z1));
            _verts.Add(new Vector3(x0, yOffset, z1));

            // UV из атласа по номеру типа.
            GetAtlasUV(type, out float u0, out float v0, out float u1, out float v1);
            _uvs.Add(new Vector2(u0, v0));
            _uvs.Add(new Vector2(u1, v0));
            _uvs.Add(new Vector2(u1, v1));
            _uvs.Add(new Vector2(u0, v1));

            // Два треугольника, обход против часовой при взгляде сверху (+Y).
            _tris.Add(baseIndex + 0);
            _tris.Add(baseIndex + 2);
            _tris.Add(baseIndex + 1);
            _tris.Add(baseIndex + 0);
            _tris.Add(baseIndex + 3);
            _tris.Add(baseIndex + 2);
        }

        /// <summary>
        /// UV-прямоугольник ячейки атласа для типа. Тип -> линейный индекс
        /// (тип-1, т.к. 0 = «нет», не рисуется) -> (col,row). Строки считаем
        /// сверху вниз: ячейка 0 — верхний левый угол атласа (привычно для
        /// раскладки текстуры), поэтому v инвертируем.
        /// </summary>
        private void GetAtlasUV(byte type, out float u0, out float v0, out float u1, out float v1)
        {
            int index = type - 1;
            if (index < 0) index = 0;

            int col = index % _atlasCols;
            int row = index / _atlasCols;

            float cw = 1f / _atlasCols;
            float ch = 1f / _atlasRows;

            u0 = col * cw;
            u1 = u0 + cw;

            // row=0 -> верхний ряд атласа. В UV v=1 сверху, поэтому инвертируем.
            v1 = 1f - row * ch;
            v0 = v1 - ch;
        }
    }
}