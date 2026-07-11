using Shared.Messages.Core;

namespace Shared.Simulation.Blocks
{
    /// <summary>Кинематическое состояние сущности в блок-мире. Оси как в Unity: X/Z — план, Y — высота ног.</summary>
    public struct BlockMoverState
    {
        public float X;
        public float Y;
        public float Z;

        /// <summary>Вертикальная скорость (блоков/тик); сидируется из снапшота при реконсиляции.</summary>
        public float VY;

        /// <summary>Стоит на опоре (любая часть AABB над solid-поверхностью).</summary>
        public bool Grounded;

        public BlockMoverState(float x, float y, float z, bool grounded = true)
        {
            X = x;
            Y = y;
            Z = z;
            VY = 0f;
            Grounded = grounded;
        }
    }

    /// <summary>Ввод одного тика движения (будущий MoveIntent фазы B2: + бит Jump).</summary>
    public readonly struct BlockMoveInput
    {
        public readonly IntentDirection Dir;
        public readonly bool Sprint;
        public readonly bool Jump;
        public readonly bool Crawl;

        public BlockMoveInput(IntentDirection dir, bool sprint = false, bool jump = false, bool crawl = false)
        {
            Dir = dir;
            Sprint = sprint;
            Jump = jump;
            Crawl = crawl;
        }
    }
}
