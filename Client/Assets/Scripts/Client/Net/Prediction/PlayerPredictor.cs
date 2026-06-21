using System.Collections.Generic;
using Shared.Messages.Core;
using Shared.Simulation;

namespace Client.Net.Prediction
{
    /// <summary>
    /// Предсказание движения СВОЕГО игрока и сверка с сервером (reconciliation).
    ///
    /// Идея: ввод применяется локально сразу (без ожидания сервера) — игрок
    /// двигается мгновенно. Каждый применённый intent с его Sequence кладётся
    /// в буфер неподтверждённых. Когда приходит снапшот, берём авторитетную
    /// позицию сервера и его LastProcessedInput, выкидываем подтверждённые
    /// intent'ы, и переигрываем оставшиеся поверх серверной позиции. Так
    /// предсказание остаётся согласованным с сервером, а коррекция (если ввод
    /// разошёлся) применяется плавно через подмену базы.
    ///
    /// Использует тот же MovementLogic, что и сервер — поэтому при отсутствии
    /// рассинхрона переигровка даёт ровно ту же позицию, дёрганья нет.
    /// </summary>
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

        /// <summary>Предсказанная позиция своего игрока (суб-тайловая).</summary>
        public float X { get; private set; }
        public float Y { get; private set; }
        public byte Facing { get; private set; }

        private bool _initialized;

        /// <summary>
        /// Применить локальный ввод немедленно (предсказание) и запомнить его
        /// для последующей сверки. Вызывается в момент отправки intent на сервер.
        /// </summary>
        public void ApplyLocal(uint sequence, IntentDirection dir, bool sprint)
        {
            float x = X, y = Y;
            MovementLogic.Apply(ref x, ref y, dir, sprint);
            X = x;
            Y = y;
            Facing = MovementLogic.ToFacing(dir, Facing);

            _pending.Add(new PendingInput(sequence, dir, sprint));
        }

        /// <summary>
        /// Сверка с сервером. serverX/Y — авторитетная позиция нашей сущности
        /// из снапшота, lastProcessedInput — последний обработанный сервером
        /// Sequence. Выкидываем подтверждённые inputs и переигрываем хвост.
        /// </summary>
        public void Reconcile(float serverX, float serverY, byte serverFacing, uint lastProcessedInput)
        {
            // Старт: первая авторитетная позиция задаёт базу.
            if (!_initialized)
            {
                X = serverX;
                Y = serverY;
                Facing = serverFacing;
                _initialized = true;
            }

            // Выкидываем всё, что сервер уже учёл.
            _pending.RemoveAll(p => p.Sequence <= lastProcessedInput);

            // База = авторитетная позиция, поверх неё переигрываем неподтверждённое.
            float x = serverX, y = serverY;
            byte facing = serverFacing;

            foreach (var p in _pending)
            {
                MovementLogic.Apply(ref x, ref y, p.Direction, p.Sprint);
                facing = MovementLogic.ToFacing(p.Direction, facing);
            }

            X = x;
            Y = y;
            Facing = facing;
        }

        public bool IsInitialized => _initialized;
    }
}