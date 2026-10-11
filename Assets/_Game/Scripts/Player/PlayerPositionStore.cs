using UnityEngine;

/// <summary>Remembers where the player should appear after a scene change.</summary>
public static class PlayerPositionStore
{
    public static Vector3 spawnPosition = Vector3.zero;
    public static bool hasSpawnPosition;
    public static string cameFromScene = "";

    public static void SetSpawn(Vector3 position)
    {
        spawnPosition = position;
        hasSpawnPosition = true;
    }

    public static void ClearSpawn()
    {
        spawnPosition = Vector3.zero;
        hasSpawnPosition = false;
    }

    /// <summary>Returns the stored position once, then forgets it.</summary>
    public static bool TryConsumeSpawn(out Vector3 position)
    {
        position = spawnPosition;
        bool had = hasSpawnPosition;
        ClearSpawn();
        return had;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ClearSpawn();
        cameFromScene = "";
    }
}
