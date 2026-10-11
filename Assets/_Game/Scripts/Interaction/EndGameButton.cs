using CatCircuit;
using TMPro;
using UnityEngine;

/// <summary>
/// Big button that ends the game: press E → the end screen with the survey QR code.
/// Create one with  GameObject ▸ CatCircuit ▸ End Game Button  (put it in House 5).
///
/// With "Require Puzzle" set (default 5 = the Mayor's code puzzle), the button only works after that
/// puzzle is solved; before that it says what is still missing. Set it to 0 to allow ending any time.
/// </summary>
public class EndGameButton : CatInteractable
{
    [Header("End game")]
    [Tooltip("Puzzle number (GameProgress) that must be solved first. 0 = the button always works.")]
    [SerializeField] private int requirePuzzle = 5;
    [SerializeField] private string surveyUrl = EndGameScreen.DefaultSurveyUrl;

    [Header("Look")]
    [Tooltip("The part that moves down when pressed and glows when the button is ready (optional).")]
    [SerializeField] private Transform buttonCap;
    [SerializeField] private Color readyGlow = new Color(1f, 0.25f, 0.2f);
    [SerializeField] private bool showLabel = true;
    [SerializeField] private string labelText = "КРАЙ НА ИГРАТА";

    private Renderer capRenderer;
    private Vector3 capRestPos;
    private float pressedUntil;
    private bool wasReady;
    private Transform label;

    private bool Ready => requirePuzzle <= 0 ||
                         (GameProgress.Instance != null && GameProgress.Instance.IsPuzzleComplete(requirePuzzle));

    private void Awake()
    {
        EnsureInteractionTrigger(1.8f);
        if (buttonCap != null)
        {
            capRestPos = buttonCap.localPosition;
            capRenderer = buttonCap.GetComponent<Renderer>();
        }
    }

    private void Start()
    {
        if (showLabel) CreateLabel();
        UpdateGlow(true);
    }

    private void Update()
    {
        if (buttonCap != null)
        {
            bool pressed = Time.unscaledTime < pressedUntil;
            buttonCap.localPosition = capRestPos + (pressed ? Vector3.down * 0.04f : Vector3.zero);
        }
        UpdateGlow(false);
    }

    public override string GetPrompt(PlayerCarry player) =>
        Ready ? "Натисни E, за да завършиш играта" : "Първо помогни на кмета с неговите задачи!";

    public override void Interact(PlayerCarry player)
    {
        pressedUntil = Time.unscaledTime + 0.25f;
        if (!Ready)
        {
            GameAudio.Play(Sfx.Hint);
            GameUI.Toast("Бутонът ще проработи, когато решиш задачите на кмета.");
            return;
        }
        GameAudio.Play(Sfx.Switch);
        EndGameScreen.Show(surveyUrl);
    }

    // ------------------------------------------------------------------ visuals

    private void UpdateGlow(bool force)
    {
        bool ready = Ready;
        if (!force && ready == wasReady) return;
        wasReady = ready;
        if (capRenderer == null) return;

        Material m = capRenderer.material;   // instance, play mode only
        Color emission = ready ? readyGlow * 2f : Color.black;
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
    }

    private void CreateLabel()
    {
        var go = new GameObject("Label");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var tmp = go.AddComponent<TextMeshPro>();
        if (UIManager.Instance != null && UIManager.Instance.Font != null) tmp.font = UIManager.Instance.Font;
        tmp.text = labelText;
        tmp.fontSize = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.85f, 0.35f);
        tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
        label = go.transform;
    }

    private void LateUpdate()
    {
        // Keep the label turned towards the camera.
        if (label == null) return;
        Camera cam = Camera.main;
        if (cam != null) label.rotation = Quaternion.LookRotation(label.position - cam.transform.position);
    }

    /// <summary>Edit-mode setup used by the create menu.</summary>
    public void Configure(Transform cap) => buttonCap = cap;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, 1.8f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.8f, "End Game Button");
#endif
    }
}
