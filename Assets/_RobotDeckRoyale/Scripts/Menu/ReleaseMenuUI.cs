using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The one production button, built from the extracted unlabelled frame artwork.
///
/// The menu previously had two button factories: this one, which draws the
/// supplied frame, and a procedural stack of tinted rounded panels in
/// <see cref="FrontendUI"/>. Every visible control was coming from the
/// procedural one, so the extracted artwork was never actually on screen.
/// FrontendUI now delegates here, which leaves a single implementation and no
/// possibility of the two drifting apart again.
///
/// Assembly, deliberately, rather than a finished picture: the frame is a
/// nine-sliced sprite with nothing baked into it, the glyph is generated
/// geometry (<see cref="MenuActionIcon"/>), and the label is real TMP text. A
/// screenshot-style button image would lose the interaction states, the
/// localisation and the crispness at every size the frame has to stretch to.
/// </summary>
public static class ReleaseMenuUI
{
    private static Sprite frame;
    private static bool frameLookedUp;

    /// <summary>The extracted frame, loaded once. Null only if the asset is gone.</summary>
    public static Sprite Frame()
    {
        if (frameLookedUp) return frame;
        frameLookedUp = true;
        frame = Resources.Load<Sprite>("UI/Release/ButtonFrame");
        if (frame == null)
            Debug.LogError("[RELEASE UI] Resources/UI/Release/ButtonFrame is missing; buttons will render untextured.");
        return frame;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() { frame = null; frameLookedUp = false; }

    // The face tint each named control carries. Keyed by the object name the menu
    // already uses, so the deck's colour identity lives in one table instead of
    // being restated at every call site.
    private static readonly Color Teal = new Color(.10f, .86f, .74f);
    private static readonly Color Ember = new Color(1f, .45f, .26f);
    private static readonly Color Azure = new Color(.16f, .62f, .98f);
    private static readonly Color Steel = new Color(.30f, .42f, .58f);

    public static Color Tint(string id, FrontendUI.Tier tier)
    {
        switch (id)
        {
            case "PlayOnline": return Teal;
            case "Create": return Ember;
            case "Join": return Azure;
            case "Shop":
            case "Missions":
            case "Loadout":
            case "Settings": return Steel;
        }
        return tier == FrontendUI.Tier.Primary ? FrontendUI.Gold
            : tier == FrontendUI.Tier.Secondary ? Azure
            : Steel;
    }

    /// <summary>Dark ink on a light face, cream on a dark one. Decided from the tint, not per call.</summary>
    private static bool NeedsDarkText(Color face) =>
        face.r * .299f + face.g * .587f + face.b * .114f > .60f;

    public static Button Button(string id, Transform parent, string label, Vector2 min, Vector2 max,
        FrontendUI.Tier tier, string iconKind, UnityAction action, float labelSize = 30f,
        bool alwaysLabel = false, Color? tintOverride = null)
    {
        Color face = tintOverride ?? Tint(id, tier);
        bool dark = NeedsDarkText(face);
        Color labelColor = dark ? FrontendUI.Ink : FrontendUI.Cream;
        Color iconColor = dark ? FrontendUI.Ink : FrontendUI.Cream;

        RectTransform root = FrontendUI.Rect(id, parent, min, max);
        var body = root.gameObject.AddComponent<ReleaseButtonFrame>();
        body.color = face;

        bool hasIcon = !string.IsNullOrEmpty(iconKind);
        bool hasLabel = alwaysLabel || !string.IsNullOrEmpty(label);
        if (hasIcon)
        {
            // Inside the frame's own padding, so the glyph never sits on the bevel.
            RectTransform slot = hasLabel
                ? FrontendUI.Rect("Icon", root, new Vector2(.10f, .26f), new Vector2(.31f, .74f))
                : FrontendUI.Rect("Icon", root, new Vector2(.24f, .24f), new Vector2(.76f, .76f));
            var icon = slot.gameObject.AddComponent<MenuActionIcon>();
            icon.Kind = iconKind;
            icon.color = iconColor;
            icon.raycastTarget = false;
        }

        if (hasLabel)
        {
            TextMeshProUGUI text = FrontendUI.Text("Label", root, label,
                new Vector2(hasIcon ? .34f : .14f, .20f), new Vector2(.90f, .80f), labelSize, labelColor);
            text.alignment = hasIcon ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            text.outlineWidth = dark ? 0f : .14f;
        }

        root.gameObject.AddComponent<FrontendPressFeedback>();
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = body;
        ColorBlock states = button.colors;
        states.normalColor = Color.white;
        states.highlightedColor = new Color(1.06f, 1.06f, 1.06f);
        states.pressedColor = new Color(.74f, .79f, .86f);
        states.disabledColor = new Color(.44f, .48f, .55f, .7f);
        states.fadeDuration = .07f;
        button.colors = states;
        // Every visible menu control is built here, so one listener on the shared
        // factory gives complete click coverage through the existing single
        // UIAudioManager source - no per-button AudioSource, and nothing to keep
        // in sync as screens are added.
        button.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));
        button.onClick.AddListener(action);
        return button;
    }
}

/// <summary>
/// The frame sprite, with its nine-slice borders kept proportional to the button.
///
/// The artwork is 847x182 with 90/80 borders, which is wider and far shorter than
/// most controls it has to fill. Drawn at its authored border size the corners
/// alone are taller than a rail button, so Unity clamps them and the rounding
/// collapses into a squashed slab. Scaling the borders by the rect instead keeps
/// the same corner proportion from the wide PLAY button down to the small
/// settings chip.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ReleaseButtonFrame : Image
{
    private const float BorderShare = .62f;   // fraction of an edge the two borders may occupy
    private float applied = -1f;

    protected override void Awake()
    {
        base.Awake();
        sprite = ReleaseMenuUI.Frame();
        type = Type.Sliced;
        FitBorders();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        FitBorders();
    }

    private void FitBorders()
    {
        if (sprite == null) return;
        Vector4 border = sprite.border;
        float vertical = border.y + border.w;
        float horizontal = border.x + border.z;
        if (vertical <= 0f && horizontal <= 0f) return;

        Rect area = rectTransform.rect;
        if (area.height < 1f || area.width < 1f) return;

        float fit = Mathf.Max(
            vertical / (area.height * BorderShare),
            horizontal / (area.width * BorderShare));
        fit = Mathf.Max(1f, fit);

        if (Mathf.Abs(fit - applied) < .01f) return;
        applied = fit;
        pixelsPerUnitMultiplier = fit;
    }
}
