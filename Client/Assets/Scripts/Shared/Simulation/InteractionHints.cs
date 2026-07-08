using Shared.World;

namespace Shared.Simulation
{
    /// <summary>Вид подсказки основного (ЛКМ) действия по наведению на интерактивный тайл (клиентский hover-hint; MVP — лестница).</summary>
    public enum HintKind : byte
    {
        None = 0,
        ClimbUp = 1,
        ClimbDown = 2
    }

    /// <summary>Резолв подсказки основного действия по целевому тайлу — ЗЕРКАЛО серверного StairHandler (тот же
    /// ordered first-match, что и реестр IInteractionHandler). Держать в синхроне с серверными обработчиками.</summary>
    public static class InteractionHints
    {
        /// <summary>Подсказка ЛКМ по целевому тайлу: StairUp→ClimbUp, StairDown→ClimbDown; иначе None. true — есть подсказка.</summary>
        public static bool TryResolvePrimary(in Tile tile, out HintKind kind)
        {
            // Зеркало StairHandler.TryHandle (Special ЦЕЛЕВОГО тайла). Будущие двери (Openable/Open) и сущности — НЕ реализовывать здесь.
            if (tile.Special == TileSpecial.StairUp) { kind = HintKind.ClimbUp; return true; }
            if (tile.Special == TileSpecial.StairDown) { kind = HintKind.ClimbDown; return true; }
            kind = HintKind.None;
            return false;
        }
    }
}
