using System.Collections.Generic;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace Client.Net.Prediction
{
    /// <summary>Предсказание движения своего игрока и сверка с сервером (reconciliation).</summary>
    public class PlayerPredictor
    {
        private readonly struct PendingInput
        {
            public readonly uint Sequence;
            public readonly IntentDirection Direction;
            public readonly bool Sprint;

            public PendingInput(uint sequence, IntentDirection direction, bool sprint)
            {
                Sequence = sequence;
                Direction = direction;
                Sprint = sprint;
            }
        }

        private readonly List<PendingInput> _pending = new List<PendingInput>();

        // Карта для коллизии — та же, что у сервера.
        private GridMap _map;
        private int _z;

        /// <summary>Предсказанная позиция своего игрока (суб-тайловая).</summary>
        public float X { get; private set; }
        public float Y { get; private set; }
        public byte Facing { get; private set; }

        private bool _initialized;

        public bool IsInitialized => _initialized;
        public int Z => _z;

        public void SetMap(GridMap map) => _map = map;

        /// <summary>Применить ввод локально (предсказание) и запомнить для переигровки.</summary>
        public void ApplyLocal(uint sequence, IntentDirection dir, bool sprint)
        {
            float x = X, y = Y;
            MovementLogic.Apply(_map, _z, ref x, ref y, dir, sprint);
            X = x;
            Y = y;
            Facing = MovementLogic.ToFacing(dir, Facing);

            _pending.Add(new PendingInput(sequence, dir, sprint));
        }

        /// <summary>Сверка с сервером: переиграть неподтверждённые вводы поверх авторитетной позиции.</summary>
        public void Reconcile(float serverX, float serverY, int serverZ, byte serverFacing, uint lastProcessedInput)
        {
            _z = serverZ; // этаж задаёт сервер

            if (!_initialized)
            {
                X = serverX;
                Y = serverY;
                Facing = serverFacing;
                _initialized = true;
            }

            // Отбрасываем то, что сервер уже учёл.
            _pending.RemoveAll(p => p.Sequence <= lastProcessedInput);

            float x = serverX, y = serverY;
            byte facing = serverFacing;

            foreach (var p in _pending)
            {
                MovementLogic.Apply(_map, _z, ref x, ref y, p.Direction, p.Sprint);
                facing = MovementLogic.ToFacing(p.Direction, facing);
            }

            X = x;
            Y = y;
            Facing = facing;
        }
    }
}
