using UnityEngine;

/// <summary>
/// One per scene. Moves the player to the right place when the scene loads
/// (see <see cref="SceneTransition"/> for the rules).
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [Header("Default Spawn (if nothing else applies)")]
    [SerializeField] private Vector3 defaultSpawnPosition = Vector3.zero;

    /// <summary>Where a new game starts in this scene (used e.g. to place the village guide).</summary>
    public Vector3 DefaultSpawnPosition => defaultSpawnPosition;

    private void Start()
    {
        GameObject player = FindPlayer();
        if (player == null)
        {
            Debug.LogWarning("[PlayerSpawner] No player found (tag 'Player' or a PlayerCarry component).");
            return;
        }

        Vector3 position = defaultSpawnPosition;
        Quaternion? rotation = null;

        if (PlayerPositionStore.TryConsumeSpawn(out Vector3 stored))
        {
            position = stored;
        }
        else
        {
            SceneTransition backDoor = FindDoorTo(PlayerPositionStore.cameFromScene);
            if (backDoor != null && backDoor.ArrivalPoint != null)
            {
                position = backDoor.ArrivalPoint.position;
                rotation = backDoor.ArrivalPoint.rotation;
            }
        }

        // A CharacterController overrides transform changes while enabled.
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.position = LiftOutOfFloor(position, player.transform, cc);
        if (rotation.HasValue) player.transform.rotation = Quaternion.Euler(0f, rotation.Value.eulerAngles.y, 0f);
        if (cc != null) cc.enabled = true;
    }

    /// <summary>
    /// If the spawn point is below the ground (e.g. an arrival point placed too low), put the cat's feet on the
    /// ground instead of inside it.
    /// </summary>
    private static Vector3 LiftOutOfFloor(Vector3 position, Transform player, CharacterController cc)
    {
        float feetOffset = cc != null ? cc.center.y * player.lossyScale.y - cc.height * 0.5f * player.lossyScale.y : 0f;
        float feet = position.y + feetOffset;
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        float ground = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (h.transform.IsChildOf(player)) continue;
            if (h.point.y <= position.y + 1.5f && h.point.y > ground) ground = h.point.y;
        }
        if (!float.IsNegativeInfinity(ground) && feet < ground - 0.02f) position.y += ground - feet + 0.05f;
        return position;
    }

    private static GameObject FindPlayer()
    {
        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged;
        PlayerCarry carry = FindAnyObjectByType<PlayerCarry>();
        return carry != null ? carry.gameObject : null;
    }

    private static SceneTransition FindDoorTo(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        foreach (SceneTransition door in FindObjectsByType<SceneTransition>(FindObjectsSortMode.None))
            if (door.TargetSceneName == sceneName) return door;
        return null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(defaultSpawnPosition, 0.5f);
    }
}
