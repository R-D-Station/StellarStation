using System;

namespace Shared.Messages.Lifts
{
    /// <summary>Чистые запросы к таблице остановок из реестра: обе стороны считают этаж одинаково.</summary>
    public static class LiftStopTable
    {
        /// <summary>Индекс В МАССИВЕ ближайшей по высоте остановки; -1 — таблица пуста.</summary>
        public static int NearestIndex(LiftStopEntry[] stops, float y)
        {
            if (stops == null || stops.Length == 0)
                return -1;
            int best = 0;
            float bestDistance = Math.Abs(stops[0].Y - y);
            for (int i = 1; i < stops.Length; i++)
            {
                float d = Math.Abs(stops[i].Y - y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Номер ЭТАЖА ближайшей остановки; -1 — таблица пуста. Номер этажа != индекс в массиве:
        /// обслуживаются не все этажи, поэтому массив разрежен.</summary>
        public static int NearestFloor(LiftStopEntry[] stops, float y)
        {
            int i = NearestIndex(stops, y);
            return i < 0 ? -1 : stops[i].Floor;
        }

        /// <summary>Индекс в массиве по номеру этажа; -1 — этаж не обслуживается.</summary>
        public static int IndexOfFloor(LiftStopEntry[] stops, int floor)
        {
            if (stops == null)
                return -1;
            for (int i = 0; i < stops.Length; i++)
                if (stops[i].Floor == floor)
                    return i;
            return -1;
        }

        public static bool IsServed(LiftStopEntry[] stops, int floor) => IndexOfFloor(stops, floor) >= 0;

        private const float AtStopEps = 0.001f;

        /// <summary>ЕДИНСТВЕННЫЙ перевод внутреннего индекса этажа в ПОКАЗЫВАЕМЫЙ порядковый (0-базовый:
        /// подпись = +1). Внутренний индекс — номер модуля рельса, обслуживаются не все, поэтому «этаж 4»
        /// на шахте из двух дверей — это рельс, а не этаж. Незнакомый индекс сводится к ближайшему
        /// обслуживаемому НЕ ВЫШЕ него. -1 — таблица пуста.</summary>
        public static int DisplayIndex(LiftStopEntry[] stops, int floor)
        {
            if (stops == null || stops.Length == 0)
                return -1;

            int best = -1, bestFloor = 0;
            for (int i = 0; i < stops.Length; i++)
            {
                if (stops[i].Floor == floor)
                    return i;
                if (stops[i].Floor < floor && (best < 0 || stops[i].Floor > bestFloor))
                {
                    best = i;
                    bestFloor = stops[i].Floor;
                }
            }
            return best >= 0 ? best : 0;
        }

        /// <summary>Внутренний индекс ближайшей остановки НЕ ВЫШЕ высоты y — этаж, «с которого кабина уехала».
        /// Для подписи берётся он, а не <see cref="NearestFloor"/>: тот снапится по расстоянию и на полпути
        /// вверх показал бы этаж, до которого кабина ещё не доехала.</summary>
        public static int FloorAtOrBelow(LiftStopEntry[] stops, float y)
        {
            if (stops == null || stops.Length == 0)
                return -1;

            int best = -1;
            float bestY = 0f;
            for (int i = 0; i < stops.Length; i++)
            {
                if (stops[i].Y > y + AtStopEps)
                    continue;
                if (best < 0 || stops[i].Y > bestY)
                {
                    best = i;
                    bestY = stops[i].Y;
                }
            }
            return best >= 0 ? stops[best].Floor : LowestFloor(stops);
        }

        private static int LowestFloor(LiftStopEntry[] stops)
        {
            int best = stops[0].Floor;
            for (int i = 1; i < stops.Length; i++)
                if (stops[i].Floor < best)
                    best = stops[i].Floor;
            return best;
        }
    }
}
