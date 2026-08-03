using Server.Doors;

namespace Server.Network.Interaction
{
    /// <summary>Композиционный корень набора IInteractionHandler для InteractIntent (был инлайновый new[] в GameServer).</summary>
    public static class InteractionRegistry
    {
        public static IInteractionHandler[] Default(GameServer server, IDoorCommands? doors = null)
        {
            var commands = doors ?? server.Doors;
            // LiftCallHandler СТРОГО перед DoorHandler: первый TryHandle==true поглощает клик, а DoorHandler
            // отвергает External-дверь — вызов лифта до него бы не дошёл.
            return new IInteractionHandler[]
            {
                new LiftCallHandler(server, server.LiftSystem, commands),
                new DoorHandler(server, commands)
            };
        }
    }
}
