using CatCircuit;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person movement for the cat (WASD + Space), relative to the camera.
/// Movement stops while <see cref="GameInput.IsBlocked"/> (e.g. the code terminal is open).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    [Header("References")]
    public InputActionAsset InputActions;
    public CharacterController playerCharacterController;
    [SerializeField] private Transform playerCamera;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5.0f;
    [SerializeField] private float rotateSmoothTime = 0.1f;
    [SerializeField] private float jumpVelocity = 5.0f;
    [SerializeField] private float gravity = -9.81f;

    private InputAction moveAction;
    private InputAction jumpAction;
    private float turnSmoothVelocity;
    private float verticalVelocity;

    private void Awake()
    {
        if (playerCharacterController == null) playerCharacterController = GetComponent<CharacterController>();
        if (playerCamera == null && Camera.main != null) playerCamera = Camera.main.transform;

        if (InputActions == null)
        {
            Debug.LogError("[Player] Assign the Input Actions asset (InputSystem_Actions).", this);
            return;
        }
        InputActionMap map = InputActions.FindActionMap("Player", true);
        moveAction = map.FindAction("Move", true);
        jumpAction = map.FindAction("Jump", true);
    }

    private void OnEnable() { if (InputActions != null) InputActions.FindActionMap("Player")?.Enable(); }
    private void OnDisable() { if (InputActions != null) InputActions.FindActionMap("Player")?.Disable(); }

    private void Update()
    {
        if (moveAction == null) return;

        bool blocked = GameInput.IsBlocked;
        Vector2 input = blocked ? Vector2.zero : moveAction.ReadValue<Vector2>();

        UpdateVertical(!blocked && jumpAction.WasPressedThisFrame());
        Move(input);
    }

    private void Move(Vector2 input)
    {
        Vector3 direction = new Vector3(input.x, 0f, input.y).normalized;
        Vector3 vertical = Vector3.up * verticalVelocity * Time.deltaTime;

        if (direction.sqrMagnitude < 0.01f)
        {
            playerCharacterController.Move(vertical);
            return;
        }

        float cameraYaw = playerCamera != null ? playerCamera.eulerAngles.y : 0f;
        float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cameraYaw;
        float smoothAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, rotateSmoothTime);
        transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);

        Vector3 moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        playerCharacterController.Move(moveDirection * walkSpeed * Time.deltaTime + vertical);
    }

    private void UpdateVertical(bool jumpPressed)
    {
        if (playerCharacterController.isGrounded)
        {
            verticalVelocity = jumpPressed ? jumpVelocity : -1f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
    }
}
