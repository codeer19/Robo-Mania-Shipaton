using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SwarmSummonHUD : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SwarmChargeController controller;

    [Header("Controls")]
    [SerializeField] private Button summonButton;
    [SerializeField] private Image chargeFill;
    [SerializeField] private TMP_Text percentageText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Graphic readyGlow;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Presentation")]
    [SerializeField] private Color chargingColor = new Color(0.05f, 0.42f, 0.9f, 0.42f);
    [SerializeField] private Color readyColor = new Color(0.08f, 0.78f, 1f, 0.5f);
    [SerializeField] private Color chargedColor = new Color(1f, 0.58f, 0.08f, 1f);
    [SerializeField] private Color readyTextColor = new Color(0.65f, 0.97f, 1f, 1f);
    [SerializeField] private Color disabledColor = new Color(0.12f, 0.18f, 0.27f, 1f);
    [SerializeField] private Color buttonFaceColor = new Color(0.018f, 0.055f, 0.12f, 0.98f);
    [SerializeField] private Color readyFaceColor = new Color(0.14f, 0.075f, 0.018f, 0.99f);
    [SerializeField, Min(96f)] private float buttonDiameter = 154f;
    [SerializeField] private Vector2 bottomRightOffset = new Vector2(-148f, 252f);
    [SerializeField, Min(0f)] private float readyPulseSpeed = 5f;
    [SerializeField, Range(0f, 0.2f)] private float readyPulseScale = 0.06f;

    private bool subscribed;
    private bool buttonBound;
    private bool ready;
    private Vector3 baseButtonScale = Vector3.one;
    private float nextRefreshTime;
    private FortressDuelManager duelManager;
    private bool displayVisible;
    private bool presentationConfigured;
    private Image innerFace;
    private UnityEngine.UI.Outline innerFaceOutline;
    private RectTransform readyGlowRect;
    private Graphic[] glyphGraphics;
    private RawImage modelGlyph;
    private bool usesFinalArt;
    private bool summonLocked;

    /// <summary>
    /// Holds the summon button closed regardless of charge.
    ///
    /// The opening build phase brings this HUD up early, because the match phase
    /// line it hosts is where the build countdown is drawn. Summoning stays
    /// refused for that whole window - the authority drops summon requests during
    /// it too, so this is the visible half of a rule enforced in the simulation.
    /// </summary>
    public void SetSummonLocked(bool locked)
    {
        summonLocked = locked;
        // Forced, because the visibility rule itself depends on this flag.
        RefreshVisibility(true);
        Refresh();
    }

    private static Sprite circleSprite;

    private void Awake()
    {
        if (summonButton == null)
            summonButton = GetComponentInChildren<Button>(true);

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        duelManager = FindFirstObjectByType<FortressDuelManager>();

        ConfigurePresentation();
        CaptureButtonScale();
    }

    private void OnEnable()
    {
        if (controller == null)
            controller = FindFirstObjectByType<SwarmChargeController>();

        ConfigurePresentation();
        BindButton();
        Subscribe();
        RefreshVisibility(true);
        Refresh();
    }

    private void OnDisable()
    {
        UnbindButton();
        Unsubscribe();
        ResetButtonScale();
    }

    private void Update()
    {
        RefreshVisibility(false);
        if (!displayVisible)
        {
            ResetButtonScale();
            return;
        }

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + 0.15f;
            Refresh();
        }

        // E summons in bot matches too, matching the online binding
        // (OnlineCombatPresentation). Online keeps its own handler.
        if (MatchSessionContext.Type != MatchType.HumanOnline && ready && !Application.isMobilePlatform &&
            UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame)
            RequestSummon();

        if (summonButton == null)
            return;

        if (!ready)
        {
            summonButton.transform.localScale = baseButtonScale;
            return;
        }

        float pulse = 1f +
                      (0.5f + Mathf.Sin(Time.unscaledTime * readyPulseSpeed) * 0.5f) *
                      readyPulseScale;
        summonButton.transform.localScale = baseButtonScale * pulse;

        if (readyGlow != null)
        {
            Color glowColor = chargedColor;
            glowColor.a = Mathf.Lerp(
                0.12f,
                0.38f,
                0.5f + Mathf.Sin(Time.unscaledTime * readyPulseSpeed) * 0.5f);
            readyGlow.color = glowColor;

            if (readyGlowRect != null)
            {
                float glowPulse = 1f +
                                  Mathf.Sin(Time.unscaledTime * readyPulseSpeed) * 0.035f;
                readyGlowRect.localScale = Vector3.one * glowPulse;
            }
        }
    }

    public void Configure(
        SwarmChargeController chargeController,
        Button button,
        Image fill,
        TMP_Text percentLabel,
        TMP_Text stateLabel = null,
        Graphic glow = null,
        CanvasGroup group = null)
    {
        UnbindButton();
        Unsubscribe();

        controller = chargeController;
        summonButton = button;
        chargeFill = fill;
        percentageText = percentLabel;
        statusText = stateLabel;
        readyGlow = glow;
        canvasGroup = group;

        presentationConfigured = false;
        ConfigurePresentation();
        CaptureButtonScale();
        BindButton();
        Subscribe();
        Refresh();
    }

    public void Refresh()
    {
        if (controller == null)
        {
            ready = false;
            if (summonButton != null)
                summonButton.interactable = false;
            if (percentageText != null)
                percentageText.text = "0%";
            if (statusText != null)
                statusText.text = "SWARM OFFLINE";
            if (chargeFill != null)
            {
                chargeFill.fillAmount = 0f;
                chargeFill.color = disabledColor;
            }
            if (readyGlow != null)
                readyGlow.enabled = false;
            SetVisibleAlpha(0.55f);
            return;
        }

        var online = MatchSessionContext.Type == MatchType.HumanOnline ? NetworkedMatchState.Instance : null;
        int charge = online != null ? online.Charge(MatchSessionContext.LocalSide) : controller.GetCharge(FortressTeam.Blue);
        int maximum = Mathf.Max(1, controller.MaximumCharge);
        int percent = Mathf.RoundToInt(charge * 100f / maximum);
        int activeBots = online != null ? online.ActiveSpidys(MatchSessionContext.LocalSide) : controller.GetActiveSwarmCount(FortressTeam.Blue);
        ready = online != null ? online.CanSummon(MatchSessionContext.LocalSide) : controller.CanSummon(FortressTeam.Blue);

        if (summonButton != null)
            summonButton.interactable = ready && !summonLocked;

        if (chargeFill != null)
        {
            chargeFill.fillAmount = Mathf.Clamp01((float)charge / maximum);
            Color fillColour = ready ? chargedColor : chargingColor;
            fillColour.a = ready ? 1f : 0.94f;
            chargeFill.color = fillColour;
        }

        if (summonButton != null && summonButton.image != null)
            summonButton.image.color = usesFinalArt ? Color.white : ready ? readyFaceColor : buttonFaceColor;

        if (innerFace != null)
            innerFace.color = ready ? readyFaceColor : buttonFaceColor;

        if (innerFaceOutline != null)
        {
            Color borderColour = ready ? chargedColor : readyColor;
            borderColour.a = ready ? 0.95f : 0.65f;
            innerFaceOutline.effectColor = borderColour;
        }

        if (glyphGraphics != null)
        {
            Color glyphColour = ready
                ? Color.Lerp(Color.white, chargedColor, 0.35f)
                : new Color(0.48f, 0.86f, 1f, 1f);
            foreach (Graphic graphic in glyphGraphics)
            {
                // The photographed model carries its own colour, so it is dimmed
                // rather than recoloured. Tinting it like the drawn shapes turned
                // the Spidy into a flat blue silhouette.
                if (graphic == null) continue;
                if (graphic == modelGlyph) graphic.color = ready ? Color.white : new Color(0.62f, 0.7f, 0.78f, 0.85f);
                else graphic.color = glyphColour;
            }
        }

        if (percentageText != null)
        {
            percentageText.text = percent + "%";
            percentageText.color = ready
                ? Color.Lerp(readyTextColor, chargedColor, 0.35f)
                : Color.white;
            percentageText.fontStyle = FontStyles.Bold;
        }

        if (statusText != null)
        {
            if (ready)
                statusText.text = "READY";
            else if (activeBots > 0)
                statusText.text = "ACTIVE x" + activeBots;
            else
                statusText.text = "CHARGING";
            statusText.color = ready
                ? Color.Lerp(readyTextColor, chargedColor, 0.35f)
                : new Color(0.68f, 0.84f, 0.94f, 1f);
        }

        if (readyGlow != null)
        {
            readyGlow.enabled = ready;
            if (ready)
                readyGlow.color = new Color(
                    chargedColor.r,
                    chargedColor.g,
                    chargedColor.b,
                    0.22f);
            else
                readyGlow.color = new Color(
                    chargedColor.r,
                    chargedColor.g,
                    chargedColor.b,
                    0f);
        }

        SetVisibleAlpha((online != null ? online.Phase == OnlineCombatPhase.Combat : controller.CombatActive) ? 1f : 0.55f);

        if (!ready)
            ResetButtonScale();
    }

    public void RequestSummon()
    {
        if (controller != null)
            controller.RequestBlueSummon();
        Refresh();
    }

    private void HandleSummonPressed()
    {
        RequestSummon();
    }

    private void Subscribe()
    {
        if (subscribed || controller == null)
            return;

        controller.ChargeChanged += HandleChargeChanged;
        controller.SwarmSummoned += HandleSwarmSummoned;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || controller == null)
            return;

        controller.ChargeChanged -= HandleChargeChanged;
        controller.SwarmSummoned -= HandleSwarmSummoned;
        subscribed = false;
    }

    private void BindButton()
    {
        if (buttonBound || summonButton == null)
            return;

        summonButton.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));
        summonButton.onClick.AddListener(HandleSummonPressed);
        buttonBound = true;
    }

    private void UnbindButton()
    {
        if (!buttonBound || summonButton == null)
            return;

        summonButton.onClick.RemoveListener(HandleSummonPressed);
        buttonBound = false;
    }

    private void CaptureButtonScale()
    {
        baseButtonScale = summonButton != null
            ? summonButton.transform.localScale
            : Vector3.one;
    }

    private void ConfigurePresentation()
    {
        if (summonButton == null)
            return;

        if (presentationConfigured)
            return;

        presentationConfigured = true;
        usesFinalArt = false; // Face, Spidy glyph and live progress are separate UI elements.

        Sprite circularSprite = GetCircleSprite();
        buttonFaceColor = new Color(.20f,.15f,.30f,1);
        readyFaceColor = new Color(.40f,.24f,.10f,1);
        readyTextColor = new Color(1,.96f,.80f,1);
        chargingColor = new Color(.98f,.59f,.14f,1);
        readyPulseScale = .025f;
        float diameter = Mathf.Max(96f, buttonDiameter);
        Vector2 anchoredOffset = bottomRightOffset == Vector2.zero
            ? new Vector2(-148f, 252f)
            : bottomRightOffset;
        RectTransform buttonRect = summonButton.transform as RectTransform;
        if (buttonRect != null)
        {
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = anchoredOffset;
            buttonRect.sizeDelta = new Vector2(diameter * 1.15f, diameter);
        }

        if (summonButton.image != null)
        {
            summonButton.image.sprite = MobileBuildHUD.RoundedPanelSprite;
            summonButton.image.type = Image.Type.Sliced;
            summonButton.image.preserveAspect = false;
            summonButton.image.color = buttonFaceColor;
            summonButton.transition = Selectable.Transition.ColorTint;
            summonButton.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colours = summonButton.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colours.pressedColor = new Color(0.72f, 0.82f, 0.9f, 1f);
            colours.selectedColor = colours.highlightedColor;
            colours.disabledColor = new Color(0.64f, 0.68f, 0.74f, 0.92f);
            colours.colorMultiplier = 1f;
            colours.fadeDuration = 0.08f;
            summonButton.colors = colours;

            UnityEngine.UI.Outline outline =
                summonButton.GetComponent<UnityEngine.UI.Outline>();
            if (outline == null)
                outline = summonButton.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = new Color(0.015f, 0.022f, 0.04f, 0.98f);
            outline.effectDistance = new Vector2(4f, -4f);
            outline.useGraphicAlpha = true;
        }

        ConfigureChargeFill(circularSprite);
        ConfigureReadyGlow(MobileBuildHUD.RoundedPanelSprite, anchoredOffset, diameter);
        EnsureInnerFace(MobileBuildHUD.RoundedPanelSprite);
        EnsureSwarmGlyph(circularSprite);
        if (innerFace != null) summonButton.targetGraphic = innerFace;
        ConfigureLabels();

        // These belonged to the old rectangular panel. They can survive a hot
        // script reload in Play Mode, so explicitly hide them when present.
        DisableLegacyAccent("HUD Accent Top");
        DisableLegacyAccent("HUD Accent Bottom");
        DisableLegacyAccent("HUD Status Divider");
    }

    private static Sprite whiteSprite;
    private static Sprite TextureWhiteSprite() => whiteSprite != null ? whiteSprite :
        whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height), Vector2.one*.5f);

    private void ConfigureChargeFill(Sprite circularSprite)
    {
        if (chargeFill == null)
            return;

        RectTransform rect = chargeFill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.anchorMin = new Vector2(.1f,.04f); rect.anchorMax = new Vector2(.9f,.09f);
        rect.sizeDelta = Vector2.zero;

        chargeFill.sprite = TextureWhiteSprite();
        chargeFill.type = Image.Type.Filled;
        chargeFill.fillMethod = Image.FillMethod.Horizontal;
        chargeFill.fillOrigin = (int)Image.Origin360.Bottom;
        chargeFill.fillClockwise = true;
        chargeFill.preserveAspect = true;
        chargeFill.raycastTarget = false;
        chargeFill.transform.SetAsLastSibling();
    }

    private void ConfigureReadyGlow(
        Sprite circularSprite,
        Vector2 anchoredOffset,
        float diameter)
    {
        if (readyGlow == null)
            return;

        readyGlowRect = readyGlow.rectTransform;
        readyGlowRect.anchorMin = new Vector2(1f, 0f);
        readyGlowRect.anchorMax = new Vector2(1f, 0f);
        readyGlowRect.pivot = new Vector2(0.5f, 0.5f);
        readyGlowRect.anchoredPosition = anchoredOffset;
        readyGlowRect.sizeDelta = Vector2.one * (diameter + 30f);
        readyGlowRect.localScale = Vector3.one;
        readyGlow.raycastTarget = false;

        if (readyGlow is Image glowImage)
        {
            glowImage.sprite = circularSprite;
            glowImage.type = Image.Type.Simple;
            glowImage.preserveAspect = true;
        }
    }

    private void EnsureInnerFace(Sprite circularSprite)
    {
        Transform existing = summonButton.transform.Find("Summon Inner Face");
        if (existing != null)
            innerFace = existing.GetComponent<Image>();

        if (innerFace == null)
        {
            GameObject face = new GameObject(
                "Summon Inner Face",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            face.transform.SetParent(summonButton.transform, false);
            innerFace = face.GetComponent<Image>();
        }

        RectTransform rect = innerFace.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(-30f, -30f);

        innerFace.sprite = circularSprite;
        innerFace.type = Image.Type.Sliced;
        innerFace.preserveAspect = false;
        innerFace.color = buttonFaceColor;
        innerFace.raycastTarget = false;

        innerFaceOutline = innerFace.GetComponent<UnityEngine.UI.Outline>();
        if (innerFaceOutline == null)
            innerFaceOutline = innerFace.gameObject.AddComponent<UnityEngine.UI.Outline>();
        innerFaceOutline.effectColor = new Color(0.08f, 0.64f, 1f, 0.68f);
        innerFaceOutline.effectDistance = new Vector2(2f, -2f);
        innerFaceOutline.useGraphicAlpha = true;

        int fillIndex = chargeFill != null
            ? chargeFill.transform.GetSiblingIndex()
            : 0;
        innerFace.transform.SetSiblingIndex(fillIndex + 1);
    }

    private void EnsureSwarmGlyph(Sprite circularSprite)
    {
        Transform glyph = summonButton.transform.Find("Swarm Glyph");
        if (glyph == null)
        {
            GameObject glyphObject = new GameObject("Swarm Glyph", typeof(RectTransform));
            glyphObject.transform.SetParent(summonButton.transform, false);
            glyph = glyphObject.transform;

            // The drawn spider is built first and kept as the fallback, so a
            // render that produces nothing still leaves a deliberate-looking
            // button rather than an empty rectangle. It is grouped so it can be
            // withdrawn as a unit: the model is a cut-out and does not cover the
            // drawn legs, so leaving both on showed stray shapes around the model.
            var drawn = new GameObject("Drawn Spider", typeof(RectTransform));
            var drawnRect = (RectTransform)drawn.transform;
            drawnRect.SetParent(glyph, false);
            drawnRect.anchorMin = Vector2.zero;
            drawnRect.anchorMax = Vector2.one;
            drawnRect.offsetMin = Vector2.zero;
            drawnRect.offsetMax = Vector2.zero;
            CreateDrawnGlyph(drawnRect, circularSprite);

            Texture2D rendered = SpidyGlyphStage.Glyph();
            if (rendered != null)
            {
                CreateModelGlyph(glyph, rendered);
                drawn.SetActive(false);
            }
        }
        modelGlyph = glyph.GetComponentInChildren<RawImage>(true);

        RectTransform glyphRect = glyph as RectTransform;
        glyphRect.anchorMin = new Vector2(0.5f, 0.5f);
        glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
        glyphRect.pivot = new Vector2(0.5f, 0.5f);
        glyphRect.anchoredPosition = new Vector2(0f, 25f);
        glyphRect.sizeDelta = new Vector2(58f, 42f);
        glyph.SetAsLastSibling();

        glyphGraphics = glyph.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in glyphGraphics)
            graphic.raycastTarget = false;
    }

    private static void CreateModelGlyph(Transform glyph, Texture2D rendered)
    {
        var iconObject = new GameObject("Spidy Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        iconObject.transform.SetParent(glyph, false);
        RectTransform rect = iconObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-7f, -9f);
        rect.offsetMax = new Vector2(7f, 9f);
        var image = iconObject.GetComponent<RawImage>();
        image.texture = rendered;
        image.raycastTarget = false;
    }

    private void CreateDrawnGlyph(Transform glyph, Sprite circularSprite)
    {
        CreateGlyphNode(glyph, "Abdomen", new Vector2(0,-3), new Vector2(22,26), circularSprite);
        CreateGlyphNode(glyph, "Head", new Vector2(0,14), new Vector2(16,15), circularSprite);
        for (int side = -1; side <= 1; side += 2)
            for (int leg = 0; leg < 3; leg++)
            {
                string name = "Leg " + side + " " + leg;
                CreateGlyphNode(glyph, name, new Vector2(side * 20, 10 - leg * 11), new Vector2(23,5), TextureWhiteSprite());
                glyph.Find(name).localRotation = Quaternion.Euler(0,0,side * (25 - leg * 25));
                CreateGlyphNode(glyph, name + " tip", new Vector2(side * 30, 7 - leg * 13), new Vector2(5,13), TextureWhiteSprite());
            }
    }

    private static void CreateGlyphNode(
        Transform parent,
        string nodeName,
        Vector2 position,
        Vector2 size,
        Sprite circularSprite)
    {
        GameObject node = new GameObject(
            nodeName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        node.transform.SetParent(parent, false);

        RectTransform rect = node.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = node.GetComponent<Image>();
        image.sprite = circularSprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = new Color(0.48f, 0.86f, 1f, 1f);
        image.raycastTarget = false;
    }

    private void ConfigureLabels()
    {
        ConfigureHudLabel(percentageText, 16f, 23f, 1.2f);
        ConfigureHudLabel(statusText, 8f, 11f, 1.1f);

        if (percentageText != null)
        {
            RectTransform rect = percentageText.rectTransform;
            rect.anchorMin = new Vector2(0.16f, 0.27f);
            rect.anchorMax = new Vector2(0.84f, 0.49f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            percentageText.transform.SetAsLastSibling();
        }

        if (statusText != null)
        {
            RectTransform rect = statusText.rectTransform;
            rect.anchorMin = new Vector2(0.12f, 0.1f);
            rect.anchorMax = new Vector2(0.88f, 0.27f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            statusText.transform.SetAsLastSibling();
        }
    }

    private static void ConfigureHudLabel(
        TMP_Text label,
        float minimumSize,
        float maximumSize,
        float spacing)
    {
        if (label == null)
            return;

        label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = minimumSize;
        label.fontSizeMax = maximumSize;
        label.characterSpacing = spacing;
        label.outlineColor = new Color(0f, 0f, 0f, 0.92f);
        label.outlineWidth = 0.16f;
        label.raycastTarget = false;
    }

    private void DisableLegacyAccent(string accentName)
    {
        if (summonButton == null)
            return;

        Transform accent = summonButton.transform.Find(accentName);
        if (accent != null)
            accent.gameObject.SetActive(false);
    }

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
            return circleSprite;

        const int textureSize = 128;
        Texture2D texture = new Texture2D(
            textureSize,
            textureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "Runtime Swarm Button Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[textureSize * textureSize];
        float centre = (textureSize - 1f) * 0.5f;
        float radius = centre - 1f;
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y),
                    new Vector2(centre, centre));
                byte alpha = (byte)Mathf.RoundToInt(
                    Mathf.Clamp01(radius - distance + 0.5f) * 255f);
                pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        circleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        circleSprite.name = "Runtime Swarm Button Circle";
        circleSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleSprite;
    }

    private bool HasCurrentCircularLayout()
    {
        RectTransform rect = summonButton.transform as RectTransform;
        return rect != null &&
               rect.anchorMin == new Vector2(1f, 0f) &&
               Mathf.Abs(rect.sizeDelta.x - rect.sizeDelta.y) < 0.5f &&
               chargeFill != null &&
               chargeFill.fillMethod == Image.FillMethod.Radial360 &&
               summonButton.transform.Find("Summon Inner Face") != null;
    }

    private void ResetButtonScale()
    {
        if (summonButton != null)
            summonButton.transform.localScale = baseButtonScale;
        if (readyGlowRect != null)
            readyGlowRect.localScale = Vector3.one;
    }

    private void RefreshVisibility(bool force)
    {
        if (duelManager == null)
            duelManager = FindFirstObjectByType<FortressDuelManager>();

        // Combat, plus the online build phase - which draws its countdown on this
        // group's Match Phase label, so the group has to be visible for it to be
        // seen at all. Keyed off summonLocked rather than the phase alone: only
        // the online build presentation sets that, so the offline build phase
        // (which has its own overhead camera and UI) stays hidden as before.
        bool shouldShow = duelManager == null ||
                          duelManager.CurrentPhase == FortressDuelPhase.Combat ||
                          (summonLocked && duelManager.CurrentPhase == FortressDuelPhase.Build);

        if (!force && shouldShow == displayVisible)
            return;

        displayVisible = shouldShow;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = displayVisible ? 1f : 0f;
            canvasGroup.interactable = displayVisible;
            canvasGroup.blocksRaycasts = displayVisible;
        }

        if (summonButton != null)
            summonButton.interactable = displayVisible && ready;
    }

    private void SetVisibleAlpha(float visibleAlpha)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = displayVisible ? visibleAlpha : 0f;
        canvasGroup.interactable = displayVisible;
        canvasGroup.blocksRaycasts = displayVisible;
    }

    private void HandleChargeChanged(
        FortressTeam team,
        int currentCharge,
        int maximumCharge)
    {
        if (team == FortressTeam.Blue)
            Refresh();
    }

    private void HandleSwarmSummoned(
        FortressTeam team,
        System.Collections.Generic.IReadOnlyList<SwarmBotAI> bots)
    {
        if (team == FortressTeam.Blue)
            Refresh();
    }
}
