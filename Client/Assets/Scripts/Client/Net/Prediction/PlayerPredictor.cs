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
            public readonly bool LayToggle;

            public PendingInput(uint sequence, IntentDirection direction, bool sprint, bool layToggle)
            {
                Sequence = sequence;
                Direction = direction;
                Sprint = sprint;
                LayToggle = layToggle;
            }
        }

        private readonly List<PendingInput> _pending = new List<PendingInput>();

        // Карта для коллизии — та же, что у сервера.
        private GridMap _map;
        private int _z;

        // База скорости/тик из СНАПШОТА (серверное число): сидируется каждый реконсайл (как State/reason); между
        // реконсайлами предсказываем последним known. Клиент НЕ реконструирует и НЕ мутирует скорость → детерминизм.
        private float _baseStep = MovementLogic.StepPerTick; // дефолт до первого снапшота

        /// <summary>Предсказанная позиция своего игрока (суб-тайловая).</summary>
        public float X { get; private set; }
        public float Y { get; private set; }
        public byte Facing { get; private set; }

        // Running-state нить: предсказанный FSM-стейт + причина лежания, сидируются из снапшота каждый реконсайл.
        private PlayerState _state;
        private LayingReason _reason; // авторитетна из снапшота; различает Voluntary (предсказуемо) vs KnockedDown
        private StatusTimers _timers; // ref-контракт FsmLogic.Step; Step его не мутирует (таймеры намеренно не реплицируются — Stun/KD авторитетны)

        /// <summary>Предсказанное FSM-состояние (байт PlayerState) — для вьюхи локального игрока.</summary>
        public byte State => (byte)_state;

        /// <summary>Причина лежания (байт LayingReason) — для вьюхи (Voluntary vs KnockedDown).</summary>
        public byte Reason => (byte)_reason;

        private bool _initialized;

        public bool IsInitialized => _initialized;
        public int Z => _z;

        public void SetMap(GridMap map) => _map = map;

        /// <summary>Применить ввод локально (предсказание) и запомнить для переигровки. Нить FSM+движение
        /// зеркалит GameServer.ProcessIntents байт-в-байт (детерминизм — иначе rubber-band).</summary>
        public void ApplyLocal(uint sequence, IntentDirection dir, bool sprint, bool layToggle)
        {
            _state = FsmLogic.Step(_state, dir, layToggle, _reason, ref _timers);
            if (FsmLogic.MovementAllowed(_state))
            {
                float x = X, y = Y;
                MovementLogic.Apply(_map, _z, ref x, ref y, dir, sprint, crawl: _state == PlayerState.Laying, baseStep: _baseStep);
                X = x;
                Y = y;
                Facing = MovementLogic.ToFacing(dir, Facing);
            }

            _pending.Add(new PendingInput(sequence, dir, sprint, layToggle));
        }

        /// <summary>Сверка с сервером: переиграть неподтверждённые вводы поверх авторитетной позиции И стейта.
        /// Стейт сидируется из снапшота (авторитетно) и переигрывается через FsmLogic.Step — State в pending НЕ
        /// кэшируется, иначе свежий серверный стейт (напр. Stun в Этапе 5) не заморозит хвост вводов.</summary>
        public void Reconcile(float serverX, float serverY, int serverZ, byte serverFacing, byte serverState, byte serverReason, float serverSpeed, uint lastProcessedInput)
        {
            _z = serverZ; // этаж задаёт сервер

            if (!_initialized)
            {
                X = serverX;
                Y = serverY;
                Facing = serverFacing;
                _initialized = true;
            }

            // Отбрасываем подтверждённое. _pending упорядочен по возрастанию Sequence (append в ApplyLocal с
            // ++_inputSequence) → подтверждённые образуют ПРЕФИКС: индексный in-place прунинг без замыкания/LINQ
            // (~30 реконсайлов/с), эквивалент RemoveAll(Sequence<=ack) ровно из-за этой сортировки.
            int prune = 0;
            while (prune < _pending.Count && _pending[prune].Sequence <= lastProcessedInput)
                prune++;
            if (prune > 0)
                _pending.RemoveRange(0, prune);

            float x = serverX, y = serverY;
            byte facing = serverFacing;
            _state = (PlayerState)serverState;    // авторитетный seed каждый реконсайл
            _reason = (LayingReason)serverReason; // причина лежания авторитетна (различает Voluntary vs KnockedDown)
            _baseStep = serverSpeed;              // эффективная скорость авторитетна из снапшота (не реконструируем)
            // _timers: таймеры намеренно не реплицируются (Stun/KnockedDown авторитетны — снапшот держит State).

            foreach (var p in _pending)
            {
                _state = FsmLogic.Step(_state, p.Direction, p.LayToggle, _reason, ref _timers);
                if (FsmLogic.MovementAllowed(_state))
                {
                    MovementLogic.Apply(_map, _z, ref x, ref y, p.Direction, p.Sprint, crawl: _state == PlayerState.Laying, baseStep: _baseStep);
                    facing = MovementLogic.ToFacing(p.Direction, facing);
                }
            }

            X = x;
            Y = y;
            Facing = facing;
        }
    }
}
