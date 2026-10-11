using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Global "is gameplay input allowed?" switch.
    /// While something (e.g. the code terminal) holds a block, the cat does not move and
    /// E / R / Q do nothing. Always pair Block() with Unblock().
    /// </summary>
    public static class GameInput
    {
        private static int blockers;

        public static bool IsBlocked => blockers > 0;

        public static void Block() => blockers++;

        public static void Unblock() => blockers = Mathf.Max(0, blockers - 1);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => blockers = 0;
    }
}
