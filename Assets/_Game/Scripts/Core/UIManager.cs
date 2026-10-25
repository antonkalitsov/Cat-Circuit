using CatCircuit;
using TMPro;
using UnityEngine;

/// <summary>
/// Screen texts. Lives on the "Canvas" object of every scene (the first one survives scene loads,
/// so settings on the Outside scene's Canvas are the ones used when the game starts there).
///
///   bottom    – interaction prompt / NPC dialogue / short messages ("toasts")
///   top-left  – held item
///   top-right – objective
///
/// From gameplay code use the static <see cref="GameUI"/> helpers – they work even if no UI exists.
/// What the bottom text shows: dialogue > toast > prompt.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI interactionPrompt;
    [SerializeField] private TextMeshProUGUI heldItemText;
    [SerializeField] private TextMeshProUGUI objectiveText;

    [Header("Readability")]
    [Tooltip("Dark rounded boxes behind the texts (added automatically).")]
    [SerializeField] private bool textBackgrounds = true;
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.05f, 0.09f, 0.78f);
    [SerializeField] private Color speakerColor = new Color(1f, 0.82f, 0.3f);

    [Header("Corner layout")]
    [Tooltip("Held item = top-left corner, objective = top-right corner (set automatically at start).")]
    [SerializeField] private bool arrangeCorners = true;
    [Tooltip("Distance from the screen edges (pixels).")]
    [SerializeField] private float cornerMargin = 24f;
    [Tooltip("Maximum width of each corner text (pixels); long objectives wrap onto a new line.")]
    [SerializeField] private float cornerWidth = 520f;
    [Tooltip("Never wider than this part of the screen, so both texts fit on small windows / the web.")]
    [Range(0.2f, 0.5f)][SerializeField] private float maxScreenFraction = 0.4f;

    private Vector2Int laidOutFor;

    private TextBackdrop promptBackdrop;

    private string prompt;
    private string dialogue;
    private string toast;
    private float toastUntil;

    /// <summary>Font that can show Bulgarian text – reused by 3D labels.</summary>
    public TMP_FontAsset Font => interactionPrompt != null ? interactionPrompt.font : null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (arrangeCorners) ArrangeCorners();
            if (textBackgrounds) AddBackdrops();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>Pins the held-item text to the top-left corner and the objective to the top-right corner.</summary>
    private void ArrangeCorners()
    {
        Pin(heldItemText, true);
        Pin(objectiveText, false);
    }

    private void Pin(TMP_Text text, bool left)
    {
        if (text == null) return;
        RectTransform rt = text.rectTransform;

        // Canvas width in UI units (works for every Canvas Scaler mode).
        Canvas canvas = text.canvas;
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float canvasWidth = Screen.width / scale;

        float inset = cornerMargin + 22f;   // leave room for the background box padding
        float width = Mathf.Min(cornerWidth, canvasWidth * maxScreenFraction);

        rt.localScale = Vector3.one;
        rt.anchorMin = rt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        rt.pivot = new Vector2(left ? 0f : 1f, 1f);
        rt.anchoredPosition = new Vector2(left ? inset : -inset, -inset);
        rt.sizeDelta = new Vector2(width, Mathf.Max(rt.sizeDelta.y, 60f));
        text.margin = Vector4.zero;          // old margins pushed the objective off the screen
        text.alignment = left ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
    }

    private void LateUpdate()
    {
        // Re-layout when the window / browser size changes.
        if (!arrangeCorners) return;
        var size = new Vector2Int(Screen.width, Screen.height);
        if (size == laidOutFor) return;
        laidOutFor = size;
        ArrangeCorners();
    }

    private void AddBackdrops()
    {
        promptBackdrop = AddBackdrop(interactionPrompt, new Vector2(32f, 18f));
        AddBackdrop(objectiveText, new Vector2(22f, 12f));
        AddBackdrop(heldItemText, new Vector2(22f, 12f));
    }

    private TextBackdrop AddBackdrop(TMP_Text text, Vector2 padding)
    {
        if (text == null) return null;
        TextBackdrop b = text.GetComponent<TextBackdrop>();
        if (b == null) b = text.gameObject.AddComponent<TextBackdrop>();
        b.Configure(backgroundColor, padding, false);
        return b;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start() => ResetUI();

    private void Update()
    {
        if (toast != null && Time.unscaledTime > toastUntil)
        {
            toast = null;
            RefreshBottom();
        }
    }

    // ------------------------------------------------------------------ bottom text

    public void ShowPrompt(string text) { prompt = string.IsNullOrEmpty(text) ? null : text; RefreshBottom(); }
    public void ClearPrompt() => ShowPrompt(null);

    public void ShowDialogue(string speaker, string line)
    {
        string hex = ColorUtility.ToHtmlStringRGB(speakerColor);
        dialogue = string.IsNullOrEmpty(speaker) ? line : $"<b><color=#{hex}>{speaker}</color></b>\n{line}";
        dialogue += $"\n<size=70%>{UIText.DialogueNext}</size>";
        RefreshBottom();
    }

    public void HideDialogue() { dialogue = null; RefreshBottom(); }

    /// <summary>True while an NPC line is on screen.</summary>
    public bool HasDialogue => dialogue != null;

    public void ShowToast(string text, float seconds = 3f)
    {
        toast = text;
        toastUntil = Time.unscaledTime + seconds;
        RefreshBottom();
    }

    private void RefreshBottom()
    {
        if (interactionPrompt == null) return;
        string text = dialogue ?? toast ?? prompt ?? "";
        interactionPrompt.text = text;
        interactionPrompt.gameObject.SetActive(true);
        // Dialogue gets a coloured stripe so it looks different from simple prompts.
        if (promptBackdrop != null) promptBackdrop.Configure(backgroundColor, dialogue != null ? new Vector2(40f, 22f) : new Vector2(32f, 18f), dialogue != null);
    }

    // ------------------------------------------------------------------ corners

    public void UpdateHeldItem(string itemName)
    {
        if (heldItemText == null) return;
        heldItemText.text = string.IsNullOrEmpty(itemName) ? "" : "Носиш: " + itemName;
    }

    public void UpdateObjective(string text)
    {
        if (objectiveText != null) objectiveText.text = text ?? "";
    }

    public void ResetUI()
    {
        prompt = dialogue = toast = null;
        RefreshBottom();
        UpdateHeldItem("");
        UpdateObjective("");
    }
}

namespace CatCircuit
{
    /// <summary>Null-safe shortcuts to <see cref="UIManager"/>. Use these from gameplay code.</summary>
    public static class GameUI
    {
        private static UIManager UI => UIManager.Instance;

        public static void Prompt(string text) { if (UI != null) UI.ShowPrompt(text); }
        public static void Dialogue(string speaker, string line) { if (UI != null) UI.ShowDialogue(speaker, line); else Debug.Log($"{speaker}: {line}"); }
        public static void HideDialogue() { if (UI != null) UI.HideDialogue(); }
        public static bool DialogueVisible => UI != null && UI.HasDialogue;
        public static void Toast(string text, float seconds = 3f) { if (UI != null) UI.ShowToast(text, seconds); else Debug.Log(text); }
        public static void Objective(string text) { if (UI != null) UI.UpdateObjective(text); }
        public static void HeldItem(string text) { if (UI != null) UI.UpdateHeldItem(text); }
        public static void Reset() { if (UI != null) UI.ResetUI(); }
    }
}
