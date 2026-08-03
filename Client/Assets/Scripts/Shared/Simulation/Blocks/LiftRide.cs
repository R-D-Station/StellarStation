namespace Shared.Simulation.Blocks
{
    /// <summary>Предикат «игрок едет на этом боксе кабины». Связь пассажир↔кабина НЕ хранится, а выводится:
    /// carry работает только при delta != 0, и хранимое поле теряло бы пассажира СТОЯЩЕЙ кабины — ровно тогда,
    /// когда он жмёт кнопку. Инвариант: всё, что везёт CarryOnMovingBox, обязано проходить и здесь.</summary>
    public static class LiftRide
    {
        /// <summary>Стоит ли игрок на боксе кабины. ⚠ ОСИ: px/py/pz — это Mover.X/Y/Z, где <b>Y = ВЫСОТА</b>.
        /// НЕ ClientConnection.X/Y/Z, где Y — план Z, а Z — целый уровень: подстановка тех полей даст тихо
        /// неверный ответ. Стоящий В ПРОЁМЕ считается пассажиром (его везёт carry, панель обязана совпадать);
        /// у кабины из одного сплошного бокса «внутри» неотличимо от «на крыше».</summary>
        public static bool StandsOn(float px, float py, float pz,
            float centerX, float centerZ, float halfX, float halfZ, float top)
        {
            float minX = px - BlockMovementConfig.HalfWidth, maxX = px + BlockMovementConfig.HalfWidth;
            float minZ = pz - BlockMovementConfig.HalfWidth, maxZ = pz + BlockMovementConfig.HalfWidth;
            float boxMinX = centerX - halfX, boxMaxX = centerX + halfX;
            float boxMinZ = centerZ - halfZ, boxMaxZ = centerZ + halfZ;

            if (!(minX < boxMaxX && maxX > boxMinX && minZ < boxMaxZ && maxZ > boxMinZ))
                return false;

            // Запас AutoStep вниз: у едущей ВВЕРХ кабины carry ловит ноги на ПРЕДЫДУЩЕЙ крыше,
            // то есть py = top - delta. Граница top - Epsilon порвала бы инвариант carry ⊆ panel.
            if (py <= top - BlockMovementConfig.AutoStep - BlockMovementConfig.Epsilon)
                return false;

            if (py <= top + BlockMovementConfig.AutoStep)
                return true;

            // В воздухе (прыжок внутри кабины) — только если ЦЕНТР игрока строго внутри плана бокса:
            // иначе «стою на этаже, кабина на блок ниже, планы касаются краем» дало бы ложный пропуск.
            return py <= top + BlockMovementConfig.StandHeight
                   && px > boxMinX && px < boxMaxX && pz > boxMinZ && pz < boxMaxZ;
        }
    }
}
