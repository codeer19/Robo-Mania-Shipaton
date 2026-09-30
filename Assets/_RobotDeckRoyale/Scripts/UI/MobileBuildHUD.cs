using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Build-phase HUD: the player's three equipped cards, the build-point budget
/// and UNDO.
///
/// The cards are the loadout chosen in the menu (slot order = keys 1, 2, 3), all
/// visible at once and large enough to tap. Each card places once; a placed card
/// is stamped USED. With a keyboard each also shows its slot key. Selection and undo go
/// through <see cref="BuildPlacementController"/>, so mouse, keys and touch share
/// one placement path. The HUD builds itself and only shows during the build phase.
/// </summary>
[RequireComponent(typeof(BuildPlacementController))]
public class MobileBuildHUD : MonoBehaviour
{
    private sealed class CardSlot
    {
        public BuildPlacementController.BuildableType Type;
        public Button Button;
        public RectTransform Rect;
        public Image Art;
        public Image Highlight;
        public GameObject Used;
    }

    [Header("Presentation")]
    [Tooltip("Card height in 1080p canvas units; width follows the card art.")]
    [SerializeField] private float cardHeight = 250f;
    [SerializeField] private float cardGap = 22f;
    [SerializeField] private float bottomMargin = 46f;
    [SerializeField] private Color labelColour = new Color(1f, 0.97f, 0.90f, 1f);

    public const float CardAspect = 256f / 320f;
    private const float LeftMargin = 56f;

    private readonly List<CardSlot> cards = new List<CardSlot>();
    private readonly List<Image> pips = new List<Image>();

    private BuildPlacementController buildController;
    private GameObject root;
    private TMP_Text pointsValue;
    private bool lastVisible;

    private void Awake()
    {
        buildController = GetComponent<BuildPlacementController>();
    }

    private void Start()
    {
        // After the controller's Awake has read the loadout.
        BuildHierarchy();
        SetVisible(false);
        Refresh();
    }

    private void OnEnable()
    {
        if (buildController != null)
            buildController.BuildStateChanged += Refresh;
    }

    private void OnDisable()
    {
        if (buildController != null)
            buildController.BuildStateChanged -= Refresh;
    }

    private void Update()
    {
        bool shouldShow = buildController != null && buildController.IsBuildPhase;
        SetVisible(shouldShow);
        if (shouldShow && !lastVisible) Refresh();
        lastVisible = shouldShow;
    }

    private void SetVisible(bool visible)
    {
        if (root != null && root.activeSelf != visible)
            root.SetActive(visible);
    }

    private void Refresh()
    {
        if (buildController == null || root == null) return;
        int points = buildController.BuildPointsRemaining;
        if (pointsValue != null) pointsValue.text = points.ToString();
        for (int i = 0; i < pips.Count; i++)
            pips[i].color = i < points ? FrontendUI.Gold : new Color(0.1f, 0.14f, 0.26f, 0.9f);

        foreach (CardSlot card in cards)
        {
            int remaining = buildController.GetRemaining(card.Type);
            bool selected = buildController.SelectedBuildable == card.Type;
            bool exhausted = remaining <= 0;
            card.Art.color = exhausted ? new Color(0.36f, 0.39f, 0.48f, 1f) : Color.white;
            card.Highlight.enabled = selected && !exhausted;
            card.Rect.localScale = Vector3.one * (selected && !exhausted ? 1.07f : 1f);
            if (card.Used.activeSelf != exhausted) card.Used.SetActive(exhausted);
            card.Button.interactable = !exhausted;
        }
    }

    // ---------------------------------------------------------------------
    // Generated UI
    // ---------------------------------------------------------------------

    private static bool KeyboardLayout =>
        !Application.isMobilePlatform && SystemInfo.deviceType != DeviceType.Handheld;

    private void BuildHierarchy()
    {
        root = new GameObject(
            "MobileBuildHUD",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the joystick canvas so build buttons take the touch instead of
        // the sticks swallowing it.
        canvas.sortingOrder = 120;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var safe = FrontendUI.Rect("Build Safe Area", root.transform, Vector2.zero, Vector2.one);
        safe.gameObject.AddComponent<FrontendSafeArea>();

        float height = Mathf.Clamp(cardHeight, 200f, 270f);
        float width = height * CardAspect;
        GameplayControlArt art = GameplayControlArt.Load();

        float x = LeftMargin;
        var loadout = buildController.Loadout;
        for (int i = 0; i < loadout.Count; i++)
        {
            var type = loadout[i];
            cards.Add(CreateCard(safe, type, i + 1, art != null ? art.Card(type) : null,
                new Vector2(x + width * 0.5f, bottomMargin + height * 0.5f), new Vector2(width, height)));
            x += width + cardGap;
        }

        SmallButton(safe, "Undo", "UNDO", new Color(0.62f, 0.16f, 0.2f),
            new Vector2(x + 10f, bottomMargin), buildController.UndoLast);

        BuildPointsPanel(safe, new Vector2(LeftMargin, bottomMargin + height + 24f));

        TMP_Text hint = FrontendUI.Text("Hint", safe, KeyboardLayout ? "1 · 2 · 3 PICK A CARD  ·  CLICK THE GROUND" : "TAP A CARD  ·  TAP THE GROUND",
            Vector2.zero, Vector2.zero, 26f, new Color(1f, 1f, 1f, 0.82f));
        hint.enableAutoSizing = false;
        hint.alignment = TextAlignmentOptions.Left;
        hint.rectTransform.pivot = new Vector2(0f, 0.5f);
        hint.rectTransform.sizeDelta = new Vector2(760f, 50f);
        hint.rectTransform.anchoredPosition = new Vector2(LeftMargin + 400f, bottomMargin + height + 24f + 35f);
    }

    private CardSlot CreateCard(RectTransform parent, BuildPlacementController.BuildableType type, int key, Sprite sprite,
        Vector2 centre, Vector2 size)
    {
        RectTransform rect = FrontendUI.Rect(type.ToString(), parent, Vector2.zero, Vector2.zero);
        rect.sizeDelta = size;
        rect.anchoredPosition = centre;

        // Selection glow sits behind the art, following the card's own corner radius.
        var highlight = FrontendUI.Panel("Selected", rect, Vector2.zero, Vector2.one, new Color(1f, 0.76f, 0.2f, 0.95f));
        highlight.rectTransform.offsetMin = new Vector2(-8f, -8f);
        highlight.rectTransform.offsetMax = new Vector2(8f, 8f);
        highlight.raycastTarget = false;

        var artObject = new GameObject("Card", typeof(RectTransform), typeof(Image));
        artObject.transform.SetParent(rect, false);
        var artRect = (RectTransform)artObject.transform;
        artRect.anchorMin = Vector2.zero; artRect.anchorMax = Vector2.one;
        artRect.offsetMin = artRect.offsetMax = Vector2.zero;
        var image = artObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        if (sprite == null)
        {
            // Art missing: still a working, labelled card.
            image.sprite = FrontendUI.Solid;
            image.color = FrontendUI.Navy;
            FrontendUI.Text("Name", artRect, BuildCards.DisplayName(type), new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.6f), 24f, labelColour);
        }

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));
        button.onClick.AddListener(() => buildController.SelectBuildableType(type));

        // One use per card: once placed the card greys out under a USED band.
        var used = FrontendUI.Panel("Used", artRect, new Vector2(-0.04f, 0.44f), new Vector2(1.04f, 0.60f), FrontendUI.Ink);
        used.raycastTarget = false;
        FrontendUI.Panel("Rule", used.transform, new Vector2(0f, 0.88f), new Vector2(1f, 1f), FrontendUI.Gold).raycastTarget = false;
        TMP_Text usedLabel = FrontendUI.Text("Label", used.transform, "USED", Vector2.zero, new Vector2(1f, 0.9f), 34f, FrontendUI.Cream);
        usedLabel.outlineWidth = 0f;
        used.gameObject.SetActive(false);

        if (KeyboardLayout)
        {
            var keyChip = FrontendUI.Panel("Key", artRect, new Vector2(0.055f, 0.845f), new Vector2(0.235f, 0.955f), FrontendUI.Cream);
            keyChip.raycastTarget = false;
            TMP_Text keyText = FrontendUI.Text("Value", keyChip.transform, key.ToString(), Vector2.zero, Vector2.one, 30f, FrontendUI.Ink);
            keyText.outlineWidth = 0f;
        }

        return new CardSlot { Type = type, Button = button, Rect = rect, Art = image, Highlight = highlight, Used = used.gameObject };
    }

    private void BuildPointsPanel(RectTransform parent, Vector2 bottomLeft)
    {
        var panel = FrontendUI.Panel("Build Points", parent, Vector2.zero, Vector2.zero, new Color(0.03f, 0.06f, 0.15f, 0.88f));
        panel.raycastTarget = false;
        var rect = panel.rectTransform;
        rect.pivot = Vector2.zero;
        rect.sizeDelta = new Vector2(380f, 70f);
        rect.anchoredPosition = bottomLeft;

        TMP_Text label = FrontendUI.Text("Label", rect, "BUILD POINTS", new Vector2(0.04f, 0.1f), new Vector2(0.56f, 0.9f), 28f, FrontendUI.Cream);
        label.alignment = TextAlignmentOptions.Left;
        pointsValue = FrontendUI.Text("Value", rect, BuildPlacementController.BuildPointsPerRound.ToString(),
            new Vector2(0.56f, 0.02f), new Vector2(0.7f, 0.98f), 50f, FrontendUI.Gold);
        for (int i = 0; i < BuildPlacementController.BuildPointsPerRound; i++)
        {
            float left = 0.72f + i * 0.09f;
            var pip = FrontendUI.Panel("Point " + (i + 1), rect, new Vector2(left, 0.3f), new Vector2(left + 0.065f, 0.7f), FrontendUI.Gold);
            pip.raycastTarget = false;
            pips.Add(pip);
        }
    }

    private Button SmallButton(RectTransform parent, string name, string label, Color colour, Vector2 bottomLeft,
        UnityEngine.Events.UnityAction action)
    {
        Button button = FrontendUI.Button(name, parent, label, Vector2.zero, Vector2.zero, colour, action);
        var rect = (RectTransform)button.transform;
        rect.pivot = Vector2.zero;
        rect.sizeDelta = new Vector2(170f, 84f);
        rect.anchoredPosition = bottomLeft;
        return button;
    }

    /// <summary>
    /// A 9-sliced rounded panel, generated so the HUD does not depend on art
    /// that does not exist yet. Shared with other gameplay HUD pieces.
    /// </summary>
    private static Sprite sharedRoundedSprite;
    public static Sprite RoundedPanelSprite => sharedRoundedSprite != null ? sharedRoundedSprite : sharedRoundedSprite = CreateRoundedSprite();
    private static Sprite CreateRoundedSprite()
    {
        const int size = 64;
        const float corner = 18f;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(corner - x, x - (size - 1 - corner), 0f);
                float dy = Mathf.Max(corner - y, y - (size - 1 - corner), 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = Mathf.Clamp01(1f - (distance - (corner - 1.5f)));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(corner, corner, corner, corner));
    }
}
