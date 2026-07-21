namespace Server.Network.Interaction
{
    public static class InteractionRegistry
    {
        public static IInteractionHandler[] Default() => new IInteractionHandler[]
        {
            new StairHandler(),
        };
    }
}
