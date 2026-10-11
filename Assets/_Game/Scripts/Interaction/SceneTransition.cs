using CatCircuit;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A door: press E inside its trigger to load another scene.
///
/// Where the player appears in the NEW scene (first match wins):
///   1. <see cref="spawnPositionOnArrival"/> if it is not (0,0,0)
///   2. the door in the new scene that leads back here, if it has an <see cref="arrivalPoint"/>
///   3. that scene's PlayerSpawner default position
/// </summary>
public class SceneTransition : CatInteractable
{
    [Header("Settings")]
    [Tooltip("Scene to load. It must be in File ▸ Build Profiles ▸ Scene List.")]
    [SerializeField] private string targetSceneName;
    [Tooltip("Exact position in the target scene. (0,0,0) = decide automatically.")]
    [SerializeField] private Vector3 spawnPositionOnArrival;
    [SerializeField] private string doorLabel = "Door";

    [Header("Arrival (this scene)")]
    [Tooltip("Where the player stands when they come BACK through this door from the target scene.")]
    [SerializeField] private Transform arrivalPoint;

    [Tooltip("Stop the player from leaving while carrying something (it would be lost).")]
    [SerializeField] private bool blockWhileCarrying = true;

    public string TargetSceneName => targetSceneName;
    public Transform ArrivalPoint => arrivalPoint;

    public override string GetPrompt(PlayerCarry player)
    {
        if (blockWhileCarrying && player.IsHoldingItem) return UIText.DropFirst;
        return UIText.Enter(doorLabel);
    }

    public override void Interact(PlayerCarry player)
    {
        if (blockWhileCarrying && player.IsHoldingItem) return;

        if (string.IsNullOrEmpty(targetSceneName) || !Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            GameUI.Toast(UIText.SceneMissing);
            Debug.LogWarning($"[SceneTransition] Scene '{targetSceneName}' is not in the build scene list.");
            return;
        }

        if (spawnPositionOnArrival != Vector3.zero) PlayerPositionStore.SetSpawn(spawnPositionOnArrival);
        else PlayerPositionStore.ClearSpawn();
        PlayerPositionStore.cameFromScene = SceneManager.GetActiveScene().name;

        GameUI.Reset();
        GameAudio.Play(Sfx.Door);
        SceneManager.LoadScene(targetSceneName);
    }
}
