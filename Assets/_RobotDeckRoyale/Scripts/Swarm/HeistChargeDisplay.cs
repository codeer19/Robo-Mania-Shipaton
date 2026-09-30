using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HeistChargeDisplay : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SwarmChargeController controller;
    [SerializeField] private FortressTeam team = FortressTeam.Blue;

    [Header("Screen Elements")]
    [SerializeField] private TMP_Text percentageText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image chargeFill;
    [SerializeField] private Graphic readyGlow;
    [SerializeField] private Transform pulseRoot;
    [SerializeField] private Damageable heistHealth;

    [Header("Team Colors")]
    [SerializeField] private Color blueColor = new Color(0.1f, 0.68f, 1f, 1f);
    [SerializeField] private Color redColor = new Color(1f, 0.18f, 0.12f, 1f);
    [SerializeField] private Color chargingColor = new Color(0.3f, 0.8f, 1f, 1f);

    [Header("Ready Pulse")]
    [SerializeField, Min(0f)] private float readyPulseSpeed = 4.5f;
    [SerializeField, Range(0f, 0.15f)] private float readyPulseScale = 0.035f;

    private Vector3 basePulseScale = Vector3.one;
    private bool subscribed;
    private bool healthSubscribed;
    private bool ready;
    private FortressDuelManager duelManager;
    private Canvas displayCanvas;
    private Camera gameplayCamera;
    private bool displayVisible;
    private Canvas worldCanvas;

    private void Awake()
    {
        displayCanvas = GetComponent<Canvas>();
        HideLegacyDisplay();
    }

    private void OnEnable()
    {
        // Charge is presented by the summon control HUD. The old world-space
        // heist screen duplicated that information and obscured the arena.
        HideLegacyDisplay();
    }

    private void OnDisable()
    {
        Unsubscribe();
        UnsubscribeHealth();
        if (pulseRoot != null)
            pulseRoot.localScale = basePulseScale;
    }

    private void Update()
    {
        HideLegacyDisplay();
    }

    private void LateUpdate()
    {
        // Intentionally empty: the legacy display remains hidden. Vault
        // WorldHealthBar components provide only the bar and numeric HP.
    }

    public void Configure(
        SwarmChargeController chargeController,
        FortressTeam displayTeam,
        TMP_Text percentLabel,
        TMP_Text stateLabel = null,
        Image fill = null,
        Graphic glow = null,
        Damageable vaultHealth = null,
        TMP_Text healthLabel = null)
    {
        Unsubscribe();
        UnsubscribeHealth();
        controller = chargeController;
        team = displayTeam;
        percentageText = percentLabel;
        statusText = stateLabel;
        chargeFill = fill;
        readyGlow = glow;
        heistHealth = vaultHealth;
        healthText = healthLabel;
        ResolveReadinessPulseRoot();
        Subscribe();
        SubscribeHealth();
        RefreshVisibility(true);
        Refresh();
    }

    public void SetTeam(FortressTeam displayTeam)
    {
        team = displayTeam;
        Refresh();
    }

    public void Refresh()
    {
        if (controller == null)
        {
            if (percentageText != null)
                percentageText.text = "E-CELL // OFFLINE";
            if (statusText != null)
                statusText.text = string.Empty;
            return;
        }

        int charge = controller.GetCharge(team);
        int maximum = Mathf.Max(1, controller.MaximumCharge);
        int percent = Mathf.RoundToInt(charge * 100f / maximum);
        int activeBots = controller.GetActiveSwarmCount(team);
        ready = controller.IsReady(team);
        Color teamColor = team == FortressTeam.Blue ? blueColor : redColor;
        // Keep a team-readable accent while charging. A shared cyan inactive
        // colour made the red heist screen look like a second blue HUD panel.
        Color displayColor = Color.Lerp(teamColor, Color.white, 0.22f);

        // ConfigurePresentation runs in Awake, before runtime setup has a
        // chance to assign the display team. Refresh the screen outline here
        // as well so a red heist never keeps the default blue accent.
        UnityEngine.UI.Outline screenOutline =
            GetComponentInChildren<UnityEngine.UI.Outline>(true);
        if (screenOutline != null)
        {
            screenOutline.effectColor = new Color(
                teamColor.r,
                teamColor.g,
                teamColor.b,
                0.82f);
        }

        if (percentageText != null)
        {
            percentageText.text =
                team.ToString().ToUpperInvariant() + " E-CELL  //  " + percent.ToString("000") + "%";
            percentageText.color = ready || activeBots > 0 ? teamColor : displayColor;
            percentageText.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        }

        if (statusText != null)
        {
            if (ready)
                statusText.text = "SWARM READY";
            else if (activeBots > 0)
                statusText.text = "SWARM DEPLOYED  //  " + activeBots;
            else
                statusText.text = "CHARGING";
            statusText.color = ready || activeBots > 0 ? teamColor : displayColor;
        }

        if (healthText != null)
        {
            healthText.text = heistHealth != null
                ? "HEIST HP  " + heistHealth.CurrentHealth.ToString("0000") +
                  " / " + heistHealth.MaxHealth.ToString("0000")
                : "HEIST HP  OFFLINE";
            healthText.color = Color.white;
        }

        if (chargeFill != null)
        {
            chargeFill.fillAmount = Mathf.Clamp01((float)charge / maximum);
            chargeFill.color = teamColor;
        }

        if (readyGlow != null)
        {
            readyGlow.enabled = ready;
            readyGlow.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.16f);
        }

        if (!ready && pulseRoot != null)
            pulseRoot.localScale = basePulseScale;
    }

    private void ResolveReadinessPulseRoot()
    {
        if (pulseRoot == null || pulseRoot == transform)
        {
            pulseRoot = readyGlow != null
                ? readyGlow.transform
                : chargeFill != null
                    ? chargeFill.transform
                    : null;
        }

        basePulseScale = pulseRoot != null
            ? pulseRoot.localScale
            : Vector3.one;
    }

    private void ResetReadinessPulse()
    {
        if (pulseRoot != null)
            pulseRoot.localScale = basePulseScale;
    }

    private void RefreshVisibility(bool force)
    {
        HideLegacyDisplay();
    }

    private void HideLegacyDisplay()
    {
        displayVisible = false;

        if (displayCanvas == null)
            displayCanvas = GetComponent<Canvas>();

        if (displayCanvas != null)
            displayCanvas.enabled = false;

        ResetReadinessPulse();
    }

    private void ConfigurePresentation()
    {
        worldCanvas = GetComponent<Canvas>();
        if (worldCanvas != null)
        {
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.worldCamera = Camera.main;
            worldCanvas.overrideSorting = true;
            worldCanvas.sortingOrder = 62;
        }

        foreach (TMP_Text label in GetComponentsInChildren<TMP_Text>(true))
        {
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            label.outlineWidth = 0.18f;
            label.raycastTarget = false;
        }

        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            image.raycastTarget = false;
        }

        UnityEngine.UI.Outline screenOutline =
            GetComponentInChildren<UnityEngine.UI.Outline>(true);
        if (screenOutline != null)
        {
            Color teamColor = team == FortressTeam.Blue ? blueColor : redColor;
            screenOutline.effectColor = new Color(
                teamColor.r,
                teamColor.g,
                teamColor.b,
                0.82f);
            screenOutline.effectDistance = new Vector2(2f, -2f);
        }
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

    private void SubscribeHealth()
    {
        if (healthSubscribed || heistHealth == null)
            return;

        heistHealth.HealthChanged += HandleHealthChanged;
        healthSubscribed = true;
    }

    private void UnsubscribeHealth()
    {
        if (!healthSubscribed || heistHealth == null)
            return;

        heistHealth.HealthChanged -= HandleHealthChanged;
        healthSubscribed = false;
    }

    private void HandleChargeChanged(
        FortressTeam changedTeam,
        int currentCharge,
        int maximumCharge)
    {
        if (changedTeam == team)
            Refresh();
    }

    private void HandleSwarmSummoned(
        FortressTeam summonedTeam,
        System.Collections.Generic.IReadOnlyList<SwarmBotAI> bots)
    {
        if (summonedTeam == team)
            Refresh();
    }

    private void HandleHealthChanged(
        Damageable target,
        int currentHealth,
        int maximumHealth)
    {
        Refresh();
    }
}
