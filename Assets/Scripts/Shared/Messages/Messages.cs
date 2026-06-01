namespace Shared.Net
{
    // =====================================================================
    // ФОРМЫ СООБЩЕНИЙ. Это ПРЕДЛОЖЕНИЕ к согласованию с владельцем сервера.
    // Граница протокола клиент<->сервер. Не финал — обсудить и зафиксировать
    // письменно вместе. Намеренно минимальные: только чтобы "труба" заработала.
    //
    // ВАЖНО: чистый C#, без UnityEngine. Координаты внутри этажа (X/Y)
    // ДРОБНЫЕ (суб-тайловое движение). Z — этаж — ЦЕЛЫЙ (дискретный).
    // ЖЁСТКОЕ ПРАВИЛО: симуляция (атмос/FOV/провода) читает только целый
    // тайл (TileX/TileY/Z). Дробь — только движение/коллизии/визуал.
    // float vs fixed-point для X/Y — решает владелец сервера (детерминизм).
    // =====================================================================

    /// <summary>Направление движения по тайлам (намерение, не позиция).</summary>
    public enum IntentDirection : byte
    {
        None = 0,
        North,  // +Y
        South,  // -Y
        East,   // +X
        West    // -X
    }

    /// <summary>
    /// Намерение игрока. Клиент шлёт ЭТО, а не позицию.
    /// Сервер сам решает, что с ним делать (валидация, движение).
    /// </summary>
    public struct MoveIntent
    {
        /// <summary>Куда игрок хочет двигаться в этот тик.</summary>
        public IntentDirection Direction;

        /// <summary>Зажат ли спринт.</summary>
        public bool Sprint;

        /// <summary>
        /// Номер тика/последовательности на клиенте. Нужен серверу и клиенту
        /// для reconciliation (откат предсказания). На этапе 0 не используется,
        /// но закладываем в протокол сразу.
        /// </summary>
        public uint Sequence;
    }

    /// <summary>Снимок одной сущности. X/Y — дробные (суб-тайл внутри этажа), Z — этаж (целое).</summary>
    public struct EntitySnapshot
    {
        public int NetId;
        public float X;      // суб-тайловая позиция внутри этажа
        public float Y;      // суб-тайловая позиция внутри этажа
        public int Z;        // этаж — ДИСКРЕТНЫЙ, не дробный
        public byte Facing;  // Entity.Direction: North=0, South=1, East=2, West=3

        // Тайл, в котором сущность находится сейчас. Симуляционные системы
        // (атмос, FOV, провода) читают ИМЕННО это, а не дробные X/Y.
        public int TileX => (int)System.MathF.Floor(X);
        public int TileY => (int)System.MathF.Floor(Y);
    }

    /// <summary>
    /// Снимок мира от сервера. На этапе 0 — плоский список сущностей.
    /// Позже: дельты, PVS (только видимые чанки), сжатие.
    /// </summary>
    public struct WorldSnapshot
    {
        /// <summary>Тик сервера, к которому относится снапшот.</summary>
        public uint ServerTick;

        /// <summary>
        /// Последний обработанный сервером Sequence ЭТОГО клиента.
        /// Нужно для reconciliation. На этапе 0 эхо от заглушки.
        /// </summary>
        public uint LastProcessedInput;

        public EntitySnapshot[] Entities;
    }
}