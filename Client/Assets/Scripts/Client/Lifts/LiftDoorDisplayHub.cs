using UnityEngine;
using Shared.Messages.Lifts;

namespace Client.Lifts
{
    public static class LiftDoorDisplayHub
    {
        public static LiftDisplayRegistry Registry { get; private set; } = new LiftDisplayRegistry();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Registry = new LiftDisplayRegistry();
    }
}
