using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class WorldHealthBar : MonoBehaviour
{
    public const float AmmoRowVerticalOffset = -25f;

    private const string NameLabelObject = "Pilot Name";
    private const string HealthLabelObject = "Health Value";
    private const string DamageTrailObject = "Damage Trail";
    private const string RespawnLabelObject = "Respawn Countdown";

    [SerializeField] private Damageable damageable;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Canvas healthCanvas;
    [SerializeField] private bool hideWhenDead = true;

    [Header("Status Plate")]
    [SerializeField] private string displayName;
    [SerializeField] private bool showName = false;
    [SerializeField] private Vector2 plateSize = new Vector2(240f, 112f);
    [SerializeField] private Vector2 healthBarSize = new Vector2(220f, 28f);
    [SerializeField] private Vector2 healthFillSize = new Vector2(204f, 18f);
    [SerializeField] private float nameVerticalOffset = 51f;
    [SerializeField] private float healthValueVerticalOffset = 25f;
    [SerializeField, Min(0f)] private float cameraDepthOffset = 0.55f;

    [Header("Health Feel")]
    [SerializeField, Min(1f)] private float healVisualSpeed = 45f;
    [SerializeField, Min(0.01f)] private float damageTrailSpeed = 0.65f;

    private int lastHealthLabel = -1;
    private Camera mainCamera;
    private FortressTarget identity;
    private TMP_Text nameText;
    private TMP_Text respawnText;
    private float displayedHealth;
    private float targetHealth;
    private bool showingRespawnCountdown;
    private Image delayedFill;
    private Transform anchorParent;
    private Vector3 anchorLocalPosition;
    private readonly List<GameObject> respawnHiddenObjects = new List<GameObject>();
    private readonly List<bool> respawnHiddenObjectStates = new List<bool>();

    private readonly Color healthyColor = new Color(0.18f, 0.96f, 0.34f, 1f);
    private readonly Color dangerColor = new Color(1f, 0.19f, 0.1f, 1f);
    private readonly Color friendlyNameColor = new Color(0.2f, 0.78f, 1f, 1f);
    private readonly Color enemyNameColor = new Color(1f, 0.3f, 0.2f, 1f);
    private bool ownedByLocalPlayer;
    private FortressTeam colouredForTeam;
    private bool nameColoured;

    private void Awake()
    {
        if (damageable == null)
            damageable = GetComponentInParent<Damageable>();

        if (healthCanvas == null)
            healthCanvas = GetComponent<Canvas>();

        identity = GetComponentInParent<FortressTarget>();
        // Only the player this device drives carries a RobotPlayerController; the
        // replicated opponent publisher and the bot carry neither.
        ownedByLocalPlayer = GetComponentInParent<RobotPlayerController>() != null;
        mainCamera = Camera.main;
        anchorParent = transform.parent;
        anchorLocalPosition = transform.localPosition;

        if (damageable != null)
        {
            displayedHealth = damageable.CurrentHealth;
            targetHealth = displayedHealth;
        }

        BuildPresentation();
    }

    private void OnEnable()
    {
        if (damageable == null)
            damageable = GetComponentInParent<Damageable>();

        if (damageable != null)
            damageable.HealthChanged += OnHealthChanged;
    }

    private void OnDisable()
    {
        if (damageable != null)
            damageable.HealthChanged -= OnHealthChanged;
    }

    private void LateUpdate()
    {
        if (damageable == null)
            return;

        if (showingRespawnCountdown)
        {
            HideNormalPresentationForRespawn();
            displayedHealth = damageable.MaxHealth;
            targetHealth = damageable.MaxHealth;
        }
        else if (displayedHealth < targetHealth)
        {
            displayedHealth = Mathf.MoveTowards(
                displayedHealth,
                targetHealth,
                healVisualSpeed * Time.deltaTime
            );
        }
        else
        {
            // Damage is immediate; healing is intentionally eased.
            displayedHealth = targetHealth;
        }

        float healthPercent = damageable.MaxHealth > 0
            ? Mathf.Clamp01(displayedHealth / damageable.MaxHealth)
            : 0f;

        if (fillImage != null)
        {
            fillImage.fillAmount = healthPercent;
            float healthyBlend = Mathf.SmoothStep(0f, 1f, healthPercent / 0.42f);
            fillImage.color = Color.Lerp(dangerColor, healthyColor, healthyBlend);
        }

        if (delayedFill != null)
        {
            if (delayedFill.fillAmount < healthPercent)
            {
                delayedFill.fillAmount = healthPercent;
            }
            else
            {
                delayedFill.fillAmount = Mathf.MoveTowards(
                    delayedFill.fillAmount,
                    healthPercent,
                    Time.deltaTime * damageTrailSpeed
                );
            }
        }

        int healthLabel = Mathf.CeilToInt(displayedHealth);
        if (healthText != null && lastHealthLabel != healthLabel)
        { lastHealthLabel = healthLabel; healthText.text = healthLabel.ToString(); }

        RefreshCanvasVisibility();
        RefreshNameColour();

        FaceGameplayCamera();
    }

    /// <summary>
    /// Paints the pilot name from the viewer's point of view.
    ///
    /// Two things made this wrong before. It ran once in Awake, which is earlier
    /// than the team assignment it reads - the arena's authored FortressTarget on
    /// the local PlayerRoot still said Blue at that point. And it derived the
    /// colour purely from that world-side value, so a player dealt SideB matched
    /// "not friendly" and read their own name in the enemy colour. The side you
    /// were dealt is gameplay truth and is left alone; who is looking decides the
    /// colour, and the local player is never their own enemy.
    /// </summary>
    private void RefreshNameColour()
    {
        if (nameText == null) return;
        FortressTeam team = identity != null ? identity.Team : FortressTeam.Blue;
        if (nameColoured && colouredForTeam == team) return;
        colouredForTeam = team;
        nameColoured = true;

        bool enemy = !ownedByLocalPlayer && (MatchSessionContext.Type == MatchType.HumanOnline
            ? !TeamSides.IsFriendly(TeamSides.FromSceneTeam(team))
            : team == FortressTeam.Red);
        nameText.color = enemy ? enemyNameColor : friendlyNameColor;
    }

    private void BuildPresentation()
    {
        EnsureFallbackImages();
        RectTransform plate = transform as RectTransform;

        if (plate != null)
            plate.sizeDelta = plateSize;

        ConfigureCanvasInteraction();
        ConfigureHealthImages();

        if (showName)
        {
            nameText = GetOrCreateText(
                NameLabelObject,
                new Vector2(plateSize.x, 38f),
                new Vector2(0f, nameVerticalOffset),
                26f
            );
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 18f;
            nameText.fontSizeMax = 26f;
            nameText.text = ResolveDisplayName();
            RefreshNameColour();
        }

        if (healthText == null)
        {
            healthText = GetOrCreateText(
                HealthLabelObject,
                new Vector2(plateSize.x, 28f),
                new Vector2(0f, healthValueVerticalOffset),
                25f
            );
        }

        if (healthText != null)
            ConfigureTextStyle(healthText, 25f);

        respawnText = GetOrCreateText(
            RespawnLabelObject,
            new Vector2(plateSize.x + 80f, 42f),
            new Vector2(0f, 90f),
            31f
        );
        respawnText.enableAutoSizing = true;
        respawnText.fontSizeMin = 23f;
        respawnText.fontSizeMax = 31f;
        respawnText.color = Color.white;
        respawnText.gameObject.SetActive(false);

        BuildDamageTrail();
    }

    private void EnsureFallbackImages()
    {
        if (fillImage != null)
            return;

        RectTransform backgroundRect = CreateImageChild(
            "Background",
            healthBarSize,
            new Color(0.018f, 0.028f, 0.055f, 0.98f));
        Image background = backgroundRect.GetComponent<Image>();
        background.raycastTarget = false;

        RectTransform fillRect = CreateImageChild(
            "Fill",
            healthFillSize,
            new Color(0.18f, 0.96f, 0.34f, 1f));
        fillImage = fillRect.GetComponent<Image>();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.raycastTarget = false;

        if (healthCanvas == null)
            healthCanvas = GetComponent<Canvas>();

        if (healthCanvas != null)
        {
            healthCanvas.renderMode = RenderMode.WorldSpace;
            healthCanvas.worldCamera = Camera.main;
        }
    }

    private RectTransform CreateImageChild(string objectName, Vector2 size, Color color)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        imageObject.transform.SetParent(transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        imageObject.GetComponent<Image>().color = color;
        return rect;
    }

    private void ConfigureCanvasInteraction()
    {
        if (healthCanvas != null)
        {
            healthCanvas.overrideSorting = true;
            healthCanvas.sortingOrder = 45;
        }

        GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();

        if (raycaster != null)
            raycaster.enabled = false;
    }

    private void ConfigureHealthImages()
    {
        if (fillImage == null || fillImage.transform.parent == null)
            return;

        RectTransform backgroundRect = fillImage.transform.parent as RectTransform;
        Image background = fillImage.transform.parent.GetComponent<Image>();

        if (backgroundRect != null)
        {
            backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = healthBarSize;
        }

        if (background != null)
        {
            background.color = new Color(0.025f, 0.04f, 0.075f, 0.97f);
            background.raycastTarget = false;

            UnityEngine.UI.Outline outline =
                background.GetComponent<UnityEngine.UI.Outline>();

            if (outline == null)
                outline = background.gameObject.AddComponent<UnityEngine.UI.Outline>();

            outline.effectColor = new Color(0f, 0f, 0f, 0.92f);
            outline.effectDistance = new Vector2(3f, -3f);
            outline.useGraphicAlpha = true;
        }

        RectTransform fillRect = fillImage.rectTransform;
        fillRect.anchorMin = new Vector2(0.5f, 0.5f);
        fillRect.anchorMax = new Vector2(0.5f, 0.5f);
        fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = healthFillSize;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.raycastTarget = false;
    }

    private TMP_Text GetOrCreateText(
        string objectName,
        Vector2 size,
        Vector2 position,
        float fontSize)
    {
        Transform existing = transform.Find(objectName);
        TextMeshProUGUI text;

        if (existing != null)
        {
            text = existing.GetComponent<TextMeshProUGUI>();

            if (text == null)
                text = existing.gameObject.AddComponent<TextMeshProUGUI>();
        }
        else
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            textObject.transform.SetParent(transform, false);
            text = textObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        ConfigureTextStyle(text, fontSize);
        return text;
    }

    private static void ConfigureTextStyle(TMP_Text text, float fontSize)
    {
        if (text == null)
            return;

        if (text.font == null && TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.outlineColor = new Color32(0, 0, 0, 255);
        text.outlineWidth = 0.22f;
    }

    private void BuildDamageTrail()
    {
        if (fillImage == null || fillImage.transform.parent == null)
            return;

        Transform existing = fillImage.transform.parent.Find(DamageTrailObject);

        if (existing != null)
        {
            delayedFill = existing.GetComponent<Image>();
        }
        else
        {
            GameObject delayedObject = Instantiate(
                fillImage.gameObject,
                fillImage.transform.parent
            );
            delayedObject.name = DamageTrailObject;
            delayedFill = delayedObject.GetComponent<Image>();
        }

        if (delayedFill == null)
            return;

        delayedFill.transform.SetSiblingIndex(fillImage.transform.GetSiblingIndex());
        delayedFill.color = new Color(1f, 0.72f, 0.12f, 0.95f);
        delayedFill.raycastTarget = false;
        delayedFill.fillAmount = damageable != null && damageable.MaxHealth > 0
            ? (float)damageable.CurrentHealth / damageable.MaxHealth
            : 1f;
        fillImage.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Re-reads the name label.
    ///
    /// The label is written once while the plate is built, in OnEnable. The
    /// fallback bot picks its name in Start, which runs later, so a plate built
    /// for the AI opponent would otherwise keep the authored placeholder for the
    /// whole match instead of the name the bot was actually given.
    /// </summary>
    public void RefreshDisplayName()
    {
        if (nameText != null)
            nameText.text = ResolveDisplayName();
    }

    private string ResolveDisplayName()
    {
        if (MatchSessionContext.Type == MatchType.HumanOnline && identity != null)
        {
            if (identity.TargetType == FortressTargetType.Vault)
                return TeamSides.IsFriendly(TeamSides.FromSceneTeam(identity.Team)) ? "YOUR HEIST" : "ENEMY HEIST";
            var avatar = GetComponentInParent<NetworkedPlayerAvatar>();
            if (avatar != null) return avatar.DisplayName.ToString();
        }
        if (MatchSessionContext.Type == MatchType.HumanOnline &&
            identity != null && identity.TargetType == FortressTargetType.Robot &&
            GetComponentInParent<RobotPlayerController>() != null)
            return MatchSessionContext.LocalDisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim().ToUpperInvariant();

        if (identity == null)
            return "ROBOT";

        if (identity.TargetType == FortressTargetType.Vault)
        {
            return identity.Team == FortressTeam.Blue
                ? "BLUE HEIST"
                : "RED HEIST";
        }

        if (identity.Team == FortressTeam.Blue)
        {
            // Same name the menu and online matches show (CrazyGames username,
            // chosen pilot name, or generated guest name).
            string pilotName = PlayerProfileService.DisplayName.Trim();

            return string.IsNullOrWhiteSpace(pilotName)
                ? "ROBO PILOT"
                : pilotName.ToUpperInvariant();
        }

        FortressDuelManager matchManager = FindFirstObjectByType<FortressDuelManager>();
        return matchManager != null
            ? matchManager.OpponentDisplayName
            : "RIVAL BOT";
    }

    private void FaceGameplayCamera()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
        {
            // Keep the world-space plate slightly in front of the robot. With a
            // pitched camera, the upper name row otherwise sits behind the mesh
            // even though the health bar itself remains visible.
            // Written only when they actually change: every transform write makes
            // the world-space canvas re-sync and re-batch, and most plates (turrets,
            // structures, idle robots) sit still under a fixed camera.
            if (anchorParent != null && transform.parent == anchorParent)
            {
                Vector3 anchorWorld = anchorParent.TransformPoint(anchorLocalPosition);
                Vector3 plate = anchorWorld - mainCamera.transform.forward * cameraDepthOffset;
                if (!transform.position.Equals(plate))
                    transform.position = plate;
            }

            Quaternion facing = mainCamera.transform.rotation;
            if (!transform.rotation.Equals(facing))
                transform.rotation = facing;
        }
    }

    private void OnHealthChanged(
        Damageable target,
        int currentHealth,
        int maxHealth)
    {
        targetHealth = currentHealth;

        if (showingRespawnCountdown && currentHealth >= maxHealth)
            SetHealthVisualImmediate(maxHealth);
    }

    /// <summary>
    /// While dead the robot carries no world UI at all. The countdown used to be
    /// drawn here, which pinned it to wherever the robot happened to die and
    /// showed one above every dead robot. It is now a single screen banner owned
    /// by CombatFeedbackOverlay.
    /// </summary>
    public void BeginRespawnCountdown()
    {
        showingRespawnCountdown = true;
        SetHealthVisualImmediate(damageable != null ? damageable.MaxHealth : 1);
        HideNormalPresentationForRespawn();

        if (healthCanvas != null)
            healthCanvas.enabled = false;

        if (respawnText != null)
            respawnText.gameObject.SetActive(false);
    }

    public void SetRespawnCountdown(int secondsRemaining)
    {
        if (!showingRespawnCountdown)
            BeginRespawnCountdown();
    }

    public void EndRespawnCountdown()
    {
        showingRespawnCountdown = false;
        SetHealthVisualImmediate(damageable != null
            ? damageable.CurrentHealth
            : 0);

        if (respawnText != null)
            respawnText.gameObject.SetActive(false);

        RestoreNormalPresentationAfterRespawn();
        RefreshCanvasVisibility();
    }

    private void HideNormalPresentationForRespawn()
    {
        for (int index = 0; index < transform.childCount; index++)
        {
            GameObject child = transform.GetChild(index).gameObject;

            if (respawnText != null && child == respawnText.gameObject)
                continue;
            if (!respawnHiddenObjects.Contains(child))
            {
                respawnHiddenObjects.Add(child);
                respawnHiddenObjectStates.Add(child.activeSelf);
            }

            child.SetActive(false);
        }
    }

    private void RestoreNormalPresentationAfterRespawn()
    {
        for (int index = 0; index < respawnHiddenObjects.Count; index++)
        {
            GameObject child = respawnHiddenObjects[index];

            if (child != null)
                child.SetActive(respawnHiddenObjectStates[index]);
        }

        respawnHiddenObjects.Clear();
        respawnHiddenObjectStates.Clear();
    }

    private void RefreshCanvasVisibility()
    {
        if (healthCanvas == null)
            return;

        bool visible = showingRespawnCountdown ||
                       !hideWhenDead ||
                       damageable == null ||
                       !damageable.IsDead;
        if (healthCanvas.enabled != visible)
            healthCanvas.enabled = visible;
    }

    private void SetHealthVisualImmediate(int health)
    {
        displayedHealth = health;
        targetHealth = health;

        float percent = damageable != null && damageable.MaxHealth > 0
            ? Mathf.Clamp01((float)health / damageable.MaxHealth)
            : 0f;

        if (fillImage != null)
            fillImage.fillAmount = percent;
        if (delayedFill != null)
            delayedFill.fillAmount = percent;
        if (healthText != null)
            healthText.text = Mathf.Max(0, health).ToString();
    }
}
