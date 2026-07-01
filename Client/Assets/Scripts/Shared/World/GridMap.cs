using System.Collections.Generic;

namespace Shared.World
{
    /// <summary>
    /// Карта мира: набор чанков по (chunkX, chunkY, z); отсутствие чанка = космос.
    /// Индексируется целыми тайловыми координатами (floor берёт вызывающий код).
    /// </summary>
    public sealed class GridMap
    {
        // Ключ чанка упакован в long: cx | cy | z.
        private readonly Dictionary<long, Chunk> _chunks = new();

        public IReadOnlyCollection<Chunk> Chunks => _chunks.Values;

        /// <summary>Ключ чанка (публичный — единый codec для внутреннего индекса И сетевого учёта стрима:
        /// сервер/тесты берут отсюда, а не реплицируют → детерминизм ключа гарантирован). 21 бит со знаком на ось.</summary>
        public static long Key(int chunkX, int chunkY, int z)
        {
            // 21 бит на ось со знаком — ~±1млн чанков по каждой.
            return ((long)(chunkX & 0x1FFFFF))
                 | ((long)(chunkY & 0x1FFFFF) << 21)
                 | ((long)(z & 0x1FFFFF) << 42);
        }

        /// <summary>Обратно из ключа в (cx,cy,z) со знаком (sign-extend с 21-го бита) — для ChunkUnload и т.п.</summary>
        public static void UnpackKey(long key, out int chunkX, out int chunkY, out int z)
        {
            chunkX = SignExtend21((int)(key & 0x1FFFFF));
            chunkY = SignExtend21((int)((key >> 21) & 0x1FFFFF));
            z = SignExtend21((int)((key >> 42) & 0x1FFFFF));
        }

        // Восстановить знак 21-битного поля (бит 20 — знаковый) — обратно к упаковке `& 0x1FFFFF`.
        private static int SignExtend21(int v) => (v & 0x100000) != 0 ? v | ~0x1FFFFF : v;

        // Floor-деление: корректно для отрицательных координат (обычное / округляет к нулю).
        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        private static int Mod(int a, int b)
        {
            int r = a % b;
            return r < 0 ? r + b : r;
        }

        public Chunk GetChunk(int chunkX, int chunkY, int z)
        {
            return _chunks.TryGetValue(Key(chunkX, chunkY, z), out var c) ? c : null;
        }

        public Chunk GetOrCreateChunk(int chunkX, int chunkY, int z)
        {
            long k = Key(chunkX, chunkY, z);
            if (!_chunks.TryGetValue(k, out var c))
            {
                c = new Chunk(chunkX, chunkY, z);
                _chunks[k] = c;
            }
            return c;
        }

        public void AddChunk(Chunk chunk) => _chunks[Key(chunk.ChunkX, chunk.ChunkY, chunk.Z)] = chunk;

        /// <summary>Удалить чанк (инкрементальный стриминг/выгрузка на клиенте). true — если чанк существовал.</summary>
        public bool RemoveChunk(int chunkX, int chunkY, int z) => _chunks.Remove(Key(chunkX, chunkY, z));

        /// <summary>Тайл по абсолютным координатам. Нет чанка — Tile.Space (запросы за край нормальны).</summary>
        public Tile GetTile(int x, int y, int z)
        {
            var c = GetChunk(FloorDiv(x, Chunk.Size), FloorDiv(y, Chunk.Size), z);
            return c == null ? Tile.Space : c[Mod(x, Chunk.Size), Mod(y, Chunk.Size)];
        }

        /// <summary>Записать тайл, создав чанк при необходимости (для редактора/загрузки).</summary>
        public void SetTile(int x, int y, int z, in Tile tile)
        {
            var c = GetOrCreateChunk(FloorDiv(x, Chunk.Size), FloorDiv(y, Chunk.Size), z);
            c[Mod(x, Chunk.Size), Mod(y, Chunk.Size)] = tile;
        }
    }
}