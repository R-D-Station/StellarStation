namespace Server.Network.Interaction
{
    /// <summary>Композиционный корень набора IInteractionHandler для InteractIntent (был инлайновый new[] в GameServer).</summary>
    public static class InteractionRegistry
    {
        public static IInteractionHandler[] Default() => new IInteractionHandler[]
        {
            new StairHandler(),
        };
    }
}
