using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The menu's shared visual vocabulary.
///
/// One rounded sprite, one frame thickness, one set of colour roles, one text
/// style. Every screen builds from these rather than choosing its own values,
/// because inconsistent radii, borders and palettes across a handful of
/// one-off widgets is precisely what makes an interface read as assembled
/// rather than designed.
/// </summary>
public static class FrontendUI
{
    // Colour roles, not just colours. Hierarchy comes from which role a control
    // is given: exactly one primary action on the screen, everything else steps
    // down. Saturated blue on every control would flatten that back out.
    public static readonly Color Ink = new Color(.045f, .09f, .20f);   // structural navy / frames
    public static readonly Color Navy = new Color(.078f, .16f, .30f);  // tertiary face
    public static readonly Color Blue = new Color(.12f, .43f, .86f);   // secondary action
    public static readonly Color Gold = new Color(1, .79f, .15f);      // primary action
    public static readonly Color Cyan = new Color(.30f, .80f, .97f);   // energy accent, used sparingly
    public static readonly Color Cream = new Color(.93f, .96f, 1f);    // text on dark

    // Frame geometry in canvas units. Constant in pixels at any button size, so
    // a small nav button and the wide primary share the same edge weight.
    public const float FrameThickness = 5f;
    public const float DepthDrop = 6f;

    private static Sprite plate;
    private static Sprite solid;
    public static Sprite Solid
    {
        get
        {
            if (solid == null)
                solid = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            return solid;
        }
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    public static Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color, bool rounded = true)
    {
        var image = Rect(name, parent, min, max).gameObject.AddComponent<Image>();
        image.color = color;
        if (rounded) { image.sprite = Plate(); image.type = Image.Type.Sliced; }
        return image;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, string value, Vector2 min, Vector2 max, float size, Color color)
    {
        var text = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        GameTypography.Apply(text);
        text.text = value; text.color = color; text.fontSize = size;
        text.enableAutoSizing = true; text.fontSizeMin = size * .65f; text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center; text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        text.outlineColor = Ink; text.outlineWidth = .16f;
        return text;
    }

    /// <summary>
    /// A labelled button in an explicit colour. The dialogs and the result screen
    /// pick their own face tint, so the tint is passed through to the shared
    /// factory rather than derived from a tier.
    /// </summary>
    public static Button Button(string name, Transform parent, string label, Vector2 min, Vector2 max, Color color, UnityAction action)
        => ReleaseMenuUI.Button(name, parent, label, min, max, Tier.Secondary, null, action, 40f,
            alwaysLabel: true, tintOverride: color);

    /// <summary>A child stretched to its parent with a constant pixel inset on each edge.</summary>
    public static Image Inset(string name, Transform parent, Color color, float left, float bottom, float right, float top)
    {
        RectTransform rect = Rect(name, parent, Vector2.zero, Vector2.one);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = Plate();
        image.type = Image.Type.Sliced;
        return image;
    }

    public enum Tier { Primary, Secondary, Tertiary }

    /// <summary>
    /// The one button used across the menu: frame, depth, face, edge, icon, label.
    ///
    /// Assembled from separate components every time rather than drawn as a
    /// finished picture, so the label is real text, the glyph is real geometry,
    /// and the interaction states are Unity's own.
    /// </summary>
    public static Button ActionButton(string name, Transform parent, string label, Vector2 min, Vector2 max,
        Tier tier, string iconKind, UnityAction action, float labelSize = 30f)
        => ReleaseMenuUI.Button(name, parent, label, min, max, tier, iconKind, action, labelSize);

    /// <summary>A text-only link. Used for the legal items, which must not compete with actions.</summary>
    public static Button TextLink(string name, Transform parent, string label, Vector2 min, Vector2 max,
        float size, UnityAction action)
    {
        RectTransform rect = Rect(name, parent, min, max);
        var hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);   // invisible but tappable: keeps the touch target honest
        var text = Text("Label", rect, label, Vector2.zero, Vector2.one, size, new Color(.74f, .83f, .93f));
        text.enableAutoSizing = false;
        text.fontStyle = FontStyles.Bold;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        var states = button.colors;
        states.normalColor = Color.white;
        states.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
        states.pressedColor = new Color(.7f, .7f, .7f);
        states.fadeDuration = .07f;
        button.colors = states;
        button.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));
        button.onClick.AddListener(action);
        return button;
    }

    private static Sprite Plate()
    {
        if (plate != null) return plate;
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "FrontendRoundedPlate", hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            var point = new Vector2(Mathf.Max(0, 8 - x, x - 23), Mathf.Max(0, 8 - y, y - 23));
            texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(8 - point.magnitude)));
        }
        texture.Apply(false, true);
        plate = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, Vector4.one * 10);
        plate.hideFlags = HideFlags.HideAndDontSave;
        return plate;
    }
}

public sealed class FrontendSafeArea : MonoBehaviour
{
    private Rect last;
    private int width, height;
    private void OnEnable() => Apply();
    private void Update()
    {
        if (last != Screen.safeArea || width != Screen.width || height != Screen.height) Apply();
    }
    private void Apply()
    {
        width = Mathf.Max(1, Screen.width); height = Mathf.Max(1, Screen.height); last = Screen.safeArea;
        var rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(last.xMin / width, last.yMin / height);
        rect.anchorMax = new Vector2(last.xMax / width, last.yMax / height);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
