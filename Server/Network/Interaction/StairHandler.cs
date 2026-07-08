using System;
using Shared.World;

namespace Server.Network.Interaction
{
    /// <summary>Лестница/трап: читает Special ЦЕЛЕВОГО тайла (StairUp→z+1, StairDown→z-1) и высаживает игрока на
    /// проходимого соседа целевого этажа. Обе стороны непроходимы → подъём/спуск кликом с соседней клетки (adjacent-use).</summary>
    public sealed class StairHandler : IInteractionHandler
    {
        public bool TryHandle(in InteractContext ctx)
        {
            var tile = ctx.Map.GetTile(ctx.TileX, ctx.TileY, ctx.TileZ);

            int targetZ;
            if (tile.Special == TileSpecial.StairUp) targetZ = ctx.TileZ + 1;
            else if (tile.Special == TileSpecial.StairDown) targetZ = ctx.TileZ - 1;
            else return false; // цель — не лестница: этот обработчик её не берёт

            // Высадка на первого walkable-соседа целевого этажа (обе стороны лестницы непроходимы); некуда — стоп.
            if (!GameServer.TryFindLanding(ctx.Map, ctx.TileX, ctx.TileY, targetZ, out int nx, out int ny))
                return true; // лестница есть, но высаживаться некуда (гард «лестница в никуда») — клик поглощён

            var client = ctx.Client;
            client.X = nx + 0.5f;
            client.Y = ny + 0.5f;
            client.Z = targetZ;
            Console.WriteLine($"[Stairs] #{client.ConnectionId}: z{ctx.TileZ} -> z{targetZ} at ({nx},{ny})");
            return true;
        }
    }
}
