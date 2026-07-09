namespace Shared.World
{
    /// <summary>Монотонный аллокатор NetId сущностей: единый пул, id никогда не переиспользуется (старт 1).</summary>
    // Одно-поточный контекст (серверный GameLoop) — без локов; при выносе аллокации в другой поток
    // заменить тело Allocate на Interlocked.Increment.
    public sealed class NetIdAllocator
    {
        private int _next = 1;

        /// <summary>Выдать следующий уникальный NetId (монотонно возрастающий, старт 1).</summary>
        public int Allocate() => _next++;
    }
}
