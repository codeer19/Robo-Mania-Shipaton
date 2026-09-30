using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First-match coaching drawn inside gameplay, never as a separate tutorial.
///
/// A compact key legend appears with the first build phase and stays into the
/// fight only until the player has both moved and fired; the build-card entry
/// leaves when building ends. The first E-Cell of the fight gets one short
/// "collect > fill > deploy" line. Nothing pauses, nothing needs a button, and
/// each hint is remembered in the profile so returning players are not coached
/// again.
/// </summary>
[DisallowMultipleComponent]
public sealed class GameplayHints : MonoBehaviour
{
    private const string ControlsHint = "Controls";
    private const string EnergyHint = "ECell";
    private const float FadeSeconds = 0.45f;
    private const float EnergyHintSeconds = 5.5f;
    private const float CombatHintTimeout = 25f;

    private FortressDuelManager arena;
    private SwarmChargeController economy;
    private RobotBlaster blaster;
    private CanvasGroup controlsGroup, energyGroup;
    private GameObject buildEntry;
    private bool controlsDone, energyDone;
    private bool moved, fired, energyShowing;
    private float combatStartedAt = -1f, energyShownAt = -1f;
    private Vector3 lastPlayerPosition;
    private float travelled;
    private int lastCellId = -1;

    private void Awake()
    {
        arena = GetComponent<FortressDuelManager>();
        economy = GetComponent<SwarmChargeController>();
        controlsDone = PlayerProfileService.IsTutorialDone(ControlsHint);
        energyDone = PlayerProfileService.IsTutorialDone(EnergyHint);
        if (controlsDone && energyDone) { enabled = false; return; }
        Build();
    }

    private void OnEnable()
    {
        if (economy != null) economy.EnergyCellSpawned += HandleCellSpawned;
    }

    private void OnDisable()
    {
        if (economy != null) economy.EnergyCellSpawned -= HandleCellSpawned;
        if (blaster != null) blaster.Fired -= HandleFired;
    }

    private void Update()
    {
        if (arena == null) return;
        FortressDuelPhase phase = arena.CurrentPhase;
        if (blaster == null && arena.LocalPlayer != null)
        {
            blaster = arena.LocalPlayer.GetComponent<RobotBlaster>();
            if (blaster != null) blaster.Fired += HandleFired;
            lastPlayerPosition = arena.LocalPlayer.transform.position;
        }

        UpdateControls(phase);
        UpdateEnergy(phase);
    }

    private void UpdateControls(FortressDuelPhase phase)
    {
        if (controlsGroup == null) return;
        if (phase == FortressDuelPhase.Combat)
        {
            if (combatStartedAt < 0f) combatStartedAt = Time.unscaledTime;
            if (arena.LocalPlayer != null && arena.LocalPlayer.enabled)
            {
                Vector3 position = arena.LocalPlayer.transform.position;
                travelled += Vector3.Distance(position, lastPlayerPosition);
                lastPlayerPosition = position;
                if (travelled > 1.5f) moved = true;
            }
        }
        if (buildEntry != null) buildEntry.SetActive(phase == FortressDuelPhase.Build);

        bool learned = moved && fired;
        bool timedOut = combatStartedAt > 0f && Time.unscaledTime - combatStartedAt > CombatHintTimeout;
        if (!controlsDone && (learned || phase == FortressDuelPhase.Finished))
        {
            controlsDone = true;
            PlayerProfileService.MarkTutorialDone(ControlsHint);
        }

        bool show = !controlsDone && !timedOut &&
            (phase == FortressDuelPhase.Build || phase == FortressDuelPhase.Rumble || phase == FortressDuelPhase.Combat);
        controlsGroup.alpha = Mathf.MoveTowards(controlsGroup.alpha, show ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
        controlsGroup.gameObject.SetActive(controlsGroup.alpha > 0.001f || show);
    }

    private void UpdateEnergy(FortressDuelPhase phase)
    {
        if (energyGroup == null) return;
        if (!energyDone && !energyShowing && phase == FortressDuelPhase.Combat && OnlineCellJustAppeared())
            BeginEnergyHint();

        if (energyShowing && (Time.unscaledTime - energyShownAt > EnergyHintSeconds || phase != FortressDuelPhase.Combat))
        {
            energyShowing = false;
            energyDone = true;
            PlayerProfileService.MarkTutorialDone(EnergyHint);
        }
        energyGroup.alpha = Mathf.MoveTowards(energyGroup.alpha, energyShowing ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
        energyGroup.gameObject.SetActive(energyGroup.alpha > 0.001f || energyShowing);
    }

    // Online the cell is a replicated record rather than a spawned pickup.
    private bool OnlineCellJustAppeared()
    {
        var state = NetworkedMatchState.Instance;
        if (state == null || state.Object == null || !state.Object.IsValid) return false;
        bool appeared = state.CellAvailable && state.CellId != lastCellId;
        lastCellId = state.CellAvailable ? state.CellId : lastCellId;
        return appeared;
    }

    private void HandleCellSpawned(EnergyCellPickup cell)
    {
        if (!energyDone && !energyShowing && arena != null && arena.CurrentPhase == FortressDuelPhase.Combat)
            BeginEnergyHint();
    }

    private void BeginEnergyHint()
    {
        energyShowing = true;
        energyShownAt = Time.unscaledTime;
        Funnel.Mark("ecell_hint_shown");
    }

    private void HandleFired(RobotBlaster _) => fired = true;

    // ------------------------------------------------------------ presentation

    private static bool TouchLayout =>
        Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;

    private void Build()
    {
        var root = new GameObject("Gameplay Hints", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 125;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var safe = FrontendUI.Rect("Safe", root.transform, Vector2.zero, Vector2.one);
        safe.gameObject.AddComponent<FrontendSafeArea>();

        if (!controlsDone)
        {
            RectTransform strip = Strip(safe, "Controls", new Vector2(0.5f, 0.855f), out controlsGroup);
            float x = 0f;
            if (TouchLayout)
            {
                Entry(strip, ref x, new[] { "LEFT STICK" }, "MOVE");
                Entry(strip, ref x, new[] { "RIGHT STICK" }, "AIM + FIRE");
                buildEntry = Entry(strip, ref x, new[] { "TAP CARD", "TAP GROUND" }, "BUILD");
            }
            else
            {
                Entry(strip, ref x, new[] { "W", "A", "S", "D" }, "MOVE");
                Entry(strip, ref x, new[] { "ARROWS", "/", "MOUSE" }, "AIM");
                Entry(strip, ref x, new[] { "SPACE", "/", "LEFT CLICK" }, "FIRE");
                buildEntry = Entry(strip, ref x, new[] { "1", "2", "3" }, "BUILD CARD");
            }
            CenterStrip(strip, x - EntryGap);
            controlsGroup.alpha = 0f;
            controlsGroup.gameObject.SetActive(false);
        }

        if (!energyDone)
        {
            RectTransform strip = Strip(safe, "Energy", new Vector2(0.5f, 0.765f), out energyGroup);
            float x = 0f;
            Step(strip, ref x, "COLLECT E-CELLS", FrontendUI.Cream, true);
            Step(strip, ref x, "FILL ENERGY", FrontendUI.Cream, true);
            Step(strip, ref x, TouchLayout ? "DEPLOY SPIDYS" : "DEPLOY SPIDYS · E", FrontendUI.Gold, false);
            CenterStrip(strip, x);
            energyGroup.alpha = 0f;
            energyGroup.gameObject.SetActive(false);
        }
    }

    private const float EntryGap = 26f;
    private const float StripHeight = 62f;

    private static RectTransform Strip(Transform parent, string name, Vector2 anchor, out CanvasGroup group)
    {
        RectTransform strip = FrontendUI.Rect(name + " Hint", parent, anchor, anchor);
        strip.sizeDelta = new Vector2(0f, StripHeight);
        group = strip.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        return strip;
    }

    // Children are laid out left to right from the strip centre; this sizes the
    // backing to the content and shifts everything so the row is centred.
    private static void CenterStrip(RectTransform strip, float contentWidth)
    {
        const float pad = 18f;
        float offset = -contentWidth * 0.5f;
        foreach (RectTransform child in strip) child.anchoredPosition += new Vector2(offset, 0f);
        strip.sizeDelta = new Vector2(contentWidth + pad * 2f, StripHeight);
        var backing = FrontendUI.Panel("Backing", strip, Vector2.zero, Vector2.one, new Color(0.02f, 0.04f, 0.10f, 0.62f));
        backing.raycastTarget = false;
        backing.transform.SetAsFirstSibling();
    }

    // Keys as small light chips, then the action in gold. Returns the entry root
    // so a caller can show or hide it as a unit.
    private static GameObject Entry(RectTransform strip, ref float x, string[] keys, string action)
    {
        RectTransform entry = FrontendUI.Rect("Entry " + action, strip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        entry.sizeDelta = Vector2.zero;
        entry.anchoredPosition = new Vector2(x, 0f);
        float local = 0f;
        foreach (string key in keys)
        {
            if (key == "/")
            {
                local += Label(entry, "/", local, 24f, new Color(0.75f, 0.82f, 0.95f)) + 2f;
                continue;
            }
            var chip = FrontendUI.Panel("Key " + key, entry, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(0.93f, 0.96f, 1f, 0.95f));
            chip.raycastTarget = false;
            TextMeshProUGUI text = FrontendUI.Text("Label", chip.transform, key, Vector2.zero, Vector2.one, 21f, FrontendUI.Ink);
            text.outlineWidth = 0f;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            float width = Mathf.Max(40f, text.GetPreferredValues(key).x + 20f);
            chip.rectTransform.sizeDelta = new Vector2(width, 40f);
            chip.rectTransform.anchoredPosition = new Vector2(local + width * 0.5f, 0f);
            local += width + 5f;
        }
        local += 7f;
        local += Label(entry, action, local, 23f, FrontendUI.Gold);
        x += local + EntryGap;
        return entry.gameObject;
    }

    private static void Step(RectTransform strip, ref float x, string text, Color color, bool chevron)
    {
        x += Label(strip, text, x, 25f, color);
        if (!chevron) return;
        x += 8f;
        x += Label(strip, "»", x, 28f, FrontendUI.Cyan) + 8f;
    }

    // Sized to the text's measured width, so no label ever wraps or clips.
    private static float Label(RectTransform parent, string value, float x, float size, Color color)
    {
        TextMeshProUGUI label = FrontendUI.Text(value, parent, value, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, color);
        label.enableAutoSizing = false;
        label.fontSize = size;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        float width = label.GetPreferredValues(value).x + 4f;
        label.rectTransform.sizeDelta = new Vector2(width, 44f);
        label.rectTransform.anchoredPosition = new Vector2(x + width * 0.5f, 0f);
        return width;
    }
}
