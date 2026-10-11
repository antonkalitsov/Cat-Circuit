using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// For full-screen UI (code terminal, pause menu): while at least one is open the cat can't move,
    /// E/R/Q do nothing, the mouse cursor is free and the camera stops turning with the mouse.
    /// Always pair Push() with Pop().
    /// </summary>
    public static class UIFocus
    {
        private static int depth;
        private static CursorLockMode savedLock;
        private static bool savedVisible;
        private static readonly List<Behaviour> pausedCameraInputs = new List<Behaviour>();

        public static bool IsActive => depth > 0;

        public static void Push()
        {
            if (depth++ > 0) return;

            GameInput.Block();
            savedLock = Cursor.lockState;
            savedVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            pausedCameraInputs.Clear();
            foreach (Behaviour b in Object.FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
            {
                if (b == null || !b.enabled) continue;
                string t = b.GetType().Name;
                if (t.Contains("InputAxisController") || t.Contains("CinemachineInputProvider"))
                {
                    b.enabled = false;
                    pausedCameraInputs.Add(b);
                }
            }
        }

        public static void Pop()
        {
            if (depth == 0) return;
            if (--depth > 0) return;

            GameInput.Unblock();
            Cursor.lockState = savedLock;
            Cursor.visible = savedVisible;
            foreach (Behaviour b in pausedCameraInputs) if (b != null) b.enabled = true;
            pausedCameraInputs.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            depth = 0;
            pausedCameraInputs.Clear();
        }
    }
}
