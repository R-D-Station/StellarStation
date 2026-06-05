using System.Collections.Generic;
using Shared.Net;

namespace Client.Net.View
{
    /// <summary>
    /// Буфер снапшотов для интерполяции. Клиент рисует чужие сущности
    /// НЕ в последней пришедшей позиции, а с небольшой задержкой, плавно
    /// интерполируя между двумя снапшотами. Это убирает рывки при суб-
    /// тайловом движении и переменном пинге.
    ///
    /// На этапе 0 (заглушка без задержки) интерполяция почти незаметна,
    /// но буфер закладываем сразу — он обязателен для реальной сети.
    /// </summary>
    public class SnapshotBuffer
    {
        private readonly struct Sample
        {
            public readonly float Time;
            public readonly float X;
            public readonly float Y;
            public readonly int Z;
            public readonly byte Facing;

            public Sample(float time, float x, float y, int z, byte facing)
            {
                Time = time; X = x; Y = y; Z = z; Facing = facing;
            }
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private const int MaxSamples = 32;

        /// <summary>Задержка интерполяции в секундах (буфер на ~2 тика при 30 TPS).</summary>
        public float InterpolationDelay = 0.066f;

        public void Push(float now, in EntitySnapshot snap)
        {
            _samples.Add(new Sample(now, snap.X, snap.Y, snap.Z, snap.Facing));
            if (_samples.Count > MaxSamples)
                _samples.RemoveAt(0);
        }

        /// <summary>
        /// Получить интерполированную позицию на момент (now - InterpolationDelay).
        /// Возвращает false, если данных ещё нет.
        /// </summary>
        public bool HaveSample(float now, out float x, out float y, out int z, out byte facing)
        {
            x = y = 0f; z = 0; facing = 0;
            if (_samples.Count == 0) return false;

            float renderTime = now - InterpolationDelay;

            // Раньше всех данных — отдаём первый.
            if (renderTime <= _samples[0].Time)
            {
                var s = _samples[0];
                x = s.X; y = s.Y; z = s.Z; facing = s.Facing;
                return true;
            }

            // Ищем пару, между которой лежит renderTime.
            for (int i = 0; i < _samples.Count - 1; i++)
            {
                var a = _samples[i];
                var b = _samples[i + 1];
                if (renderTime >= a.Time && renderTime <= b.Time)
                {
                    float span = b.Time - a.Time;
                    float t = span > 0f ? (renderTime - a.Time) / span : 0f;

                    // Z дискретный — НЕ интерполируем, берём целевой этаж.
                    x = a.X + (b.X - a.X) * t;
                    y = a.Y + (b.Y - a.Y) * t;
                    z = b.Z;
                    facing = b.Facing;
                    return true;
                }
            }

            // Позже всех данных — отдаём последний (экстраполяцию не делаем).
            var last = _samples[_samples.Count - 1];
            x = last.X; y = last.Y; z = last.Z; facing = last.Facing;
            return true;
        }
    }
}