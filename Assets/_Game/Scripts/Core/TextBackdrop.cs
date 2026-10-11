using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatCircuit
{
    /// <summary>
    /// Draws a dark rounded box behind a TextMeshPro text that grows and shrinks with the text,
    /// so it is readable on any background. Hidden when the text is empty.
    /// UIManager adds it automatically to the prompt / dialogue, objective and held-item texts.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("CatCircuit/Text Backdrop")]
    public class TextBackdrop : MonoBehaviour
    {
        [SerializeField] private Color color = new Color(0.04f, 0.05f, 0.09f, 0.78f);
        [Tooltip("Space between the text and the edge of the box (pixels).")]
        [SerializeField] private Vector2 padding = new Vector2(28f, 16f);
        [SerializeField] private int cornerRadius = 18;
        [Tooltip("Optional coloured stripe on the left, e.g. for dialogue.")]
        [SerializeField] private bool accentStripe = false;
        [SerializeField] private Color accentColor = new Color(1f, 0.8f, 0.25f, 1f);

        private TMP_Text text;
        private RectTransform box;
        private Image boxImage;
        private Image stripe;
        private string lastText;
        private static Sprite roundedSprite;
        private static int roundedSpriteRadius;

        public void Configure(Color boxColor, Vector2 boxPadding, bool withAccent)
        {
            color = boxColor;
            padding = boxPadding;
            accentStripe = withAccent;
            if (boxImage != null) boxImage.color = color;
            if (stripe != null) stripe.gameObject.SetActive(accentStripe);
            lastText = null;
        }

        private void Awake()
        {
            text = GetComponent<TMP_Text>();

            var go = new GameObject(name + " Backdrop", typeof(RectTransform));
            box = (RectTransform)go.transform;
            box.SetParent(transform.parent, false);
            box.SetSiblingIndex(transform.GetSiblingIndex());      // drawn just before (= behind) the text
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);

            boxImage = go.AddComponent<Image>();
            boxImage.sprite = RoundedSprite(cornerRadius);
            boxImage.type = Image.Type.Sliced;
            boxImage.color = color;
            boxImage.raycastTarget = false;

            var stripeGo = new GameObject("Accent", typeof(RectTransform));
            var sr = (RectTransform)stripeGo.transform;
            sr.SetParent(box, false);
            sr.anchorMin = new Vector2(0f, 0.15f);
            sr.anchorMax = new Vector2(0f, 0.85f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.sizeDelta = new Vector2(6f, 0f);
            sr.anchoredPosition = new Vector2(8f, 0f);
            stripe = stripeGo.AddComponent<Image>();
            stripe.color = accentColor;
            stripe.raycastTarget = false;
            stripeGo.SetActive(accentStripe);

            go.SetActive(false);
        }

        private void OnDisable()
        {
            if (box != null) box.gameObject.SetActive(false);
            lastText = null;
        }

        private void OnDestroy()
        {
            if (box != null) Destroy(box.gameObject);
        }

        private void LateUpdate()
        {
            if (box == null) return;
            string current = text.text;
            bool show = !string.IsNullOrWhiteSpace(current) && text.isActiveAndEnabled;
            if (box.gameObject.activeSelf != show) box.gameObject.SetActive(show);
            if (!show) { lastText = current; return; }

            if (current != lastText)
            {
                lastText = current;
                text.ForceMeshUpdate();
            }

            // Place the box around the rendered characters (works for any alignment / anchors).
            Bounds b = text.textBounds;
            if (b.size.x <= 0f || b.size.y <= 0f) { box.gameObject.SetActive(false); return; }

            box.position = text.rectTransform.TransformPoint(b.center);
            box.rotation = text.rectTransform.rotation;
            box.localScale = text.rectTransform.localScale;
            float extraLeft = accentStripe ? 12f : 0f;
            box.sizeDelta = new Vector2(b.size.x + padding.x * 2f + extraLeft, b.size.y + padding.y * 2f);
            if (extraLeft > 0f) box.position -= text.rectTransform.TransformVector(new Vector3(extraLeft * 0.5f, 0f, 0f));
        }

        /// <summary>White rounded rectangle, 9-sliced so it stretches to any size.</summary>
        private static Sprite RoundedSprite(int radius)
        {
            if (roundedSprite != null && roundedSpriteRadius == radius) return roundedSprite;
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // distance outside the inner rectangle
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            var border = new Vector4(radius + 1, radius + 1, radius + 1, radius + 1);
            roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            roundedSpriteRadius = radius;
            return roundedSprite;
        }
    }
}
