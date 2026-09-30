using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public sealed class MainMenuController : MonoBehaviour
{

    [Header("Robot Preview")]
    [SerializeField] private GameObject robotPreviewPrefab;

    // Saturated arena colours shared with the purple/orange Robo Mania world.  The menu is
    // intentionally playful and chunky; it should read as a game lobby, never an admin panel.
    private readonly Color midnight = Hex("#10275C");
    private readonly Color navy = Hex("#174E9B");
    private readonly Color panel = Hex("#245DB5");
    private readonly Color panelRaised = Hex("#3479D2");
    private readonly Color panelSoft = Hex("#5B65B7");
    private readonly Color cyan = Hex("#28E5FF");
    private readonly Color cyanDark = Hex("#0989DE");
    private readonly Color orange = Hex("#FF7B2C");
    private readonly Color yellow = Hex("#FFD52F");
    private readonly Color purple = Hex("#8B48D7");
    private readonly Color lime = Hex("#54EC83");
    private readonly Color muted = Hex("#D4EBFF");
    private readonly Color danger = Hex("#FF506D");
    private readonly Color ink = Hex("#102048");

    private readonly List<RectTransform> ambientBits = new List<RectTransform>();

    private Canvas canvas;
    private RectTransform safeRoot;
    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private TextMeshProUGUI playerNameText;
    private TextMeshProUGUI coinsText;
    private TextMeshProUGUI onlineStateText;
    private GameObject dialog;
    private TMP_InputField dialogInput;
    private TMP_Text nameError;
    private bool choosingName;
    private GameObject shopOverlay;
    private RectTransform shopTabsRoot;
    private RectTransform shopItemsRoot;
    private TextMeshProUGUI shopItemTitle;
    private TextMeshProUGUI shopItemDescription;
    private TextMeshProUGUI shopMessage;
    private Button shopActionButton;
    private TextMeshProUGUI shopActionLabel;
    private RobotCosmeticCategory shopCategory = RobotCosmeticCategory.Skin;
    private RobotCosmeticDefinition selectedCosmetic;
    private RobotPreviewPresenter previewPresenter;
    private Camera menuCamera;
    private Texture2D arenaGradientTexture;
    private Texture2D roundedPlateTexture;
    private Sprite roundedPlateSprite;
    private Texture2D radialGlowTexture;
    private Sprite radialGlowSprite;
    private Texture2D vignetteTexture;
    private Sprite vignetteSprite;
    private RectTransform heroHalo;
    private RectTransform playButtonTransform;
    private RectTransform modeStrip;

    // Runtime-only references for the lightweight menu overlays and transition
    // textures. Keeping them explicit avoids missing-reference compile errors
    // when the menu is regenerated at runtime.
    private GameObject settingsOverlay;
    private Texture2D diagonalStripeTexture;
    private CanvasGroup shopCanvasGroup;
    private CanvasGroup dialogCanvasGroup;

    // A small burst on the primary action keeps the call-to-action alive without
    // turning the lobby into a field of random decorative particles.
    private readonly RectTransform[] playStars = new RectTransform[6];
    private readonly float[] playStarLife = new float[6];
    private readonly Vector2[] playStarVel = new Vector2[6];
    private float nextPlayStarBurst;

    private void Awake()
    {
        EnsureMenuCamera();
        CreateEventSystemIfNeeded();
        BuildMenu();
        PlayerProfileService.ProfileChanged += HandleProfileChanged;
    }

    private void Start()
    {
        PlayerAudioSettings.Apply();
        Funnel.Mark("menu_seen");
    }

    /// <summary>
    /// Naming is optional. A new player used to meet a mandatory, undismissable
    /// name dialog (plus a second confirmation) before PLAY would respond, which
    /// stood between every first visit and the first match. Players now start
    /// under their CrazyGames username, or the existing generated PILOT-#### name,
    /// and can choose a pilot name later by tapping their profile.
    /// </summary>
    public bool RequireConfirmedName() => true;

    private void OpenOptionalNameDialog()
    {
        if (PlayerProfileService.HasConfirmedName || PlayerProfileService.UsesAccountName) return;
        OpenNameDialog();
    }

    private void Update()
    {
        if (safeRoot != null &&
            (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight || Screen.safeArea != lastSafeArea))
        {
            ApplySafeArea();
        }


    }

    private void AnimateHomeScreen()
    {
        float time = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        if (heroHalo != null)
        {
            float pulse = 1f + Mathf.Sin(time * 1.65f) * 0.035f;
            heroHalo.localScale = new Vector3(pulse, pulse, 1f);
            heroHalo.localRotation = Quaternion.Euler(0f, 0f, time * 2.5f);
            // Gentle color pulse on the halo image between orange and cyan-ish.
            Image haloImg = heroHalo.GetComponent<Image>();
            if (haloImg != null)
            {
                float colorT = Mathf.Sin(time * 0.8f) * 0.5f + 0.5f;
                haloImg.color = Color.Lerp(
                    new Color(1f, 0.47f, 0.16f, 0.22f),
                    new Color(0.16f, 0.90f, 1f, 0.18f),
                    colorT);
            }
        }

        if (playButtonTransform != null)
        {
            float pulse = 1f + Mathf.Sin(time * 2.2f) * 0.018f;
            playButtonTransform.localScale = new Vector3(pulse, pulse, 1f);
        }

        for (int index = 0; index < ambientBits.Count; index++)
        {
            RectTransform bit = ambientBits[index];
            if (bit == null)
            {
                continue;
            }

            float phase = index * 0.83f;
            float signalPulse = 1f + Mathf.Sin(time * 1.7f + phase) * 0.09f;
            bit.localScale = new Vector3(signalPulse, signalPulse, 1f);
        }

        // Play button star bursts — every 3 seconds, stars shoot outward.
        if (playButtonTransform != null)
        {
            if (time > nextPlayStarBurst)
            {
                nextPlayStarBurst = time + 3f;
                SpawnPlayButtonStars();
            }
            UpdatePlayButtonStars(dt);
        }
    }

    private void SpawnPlayButtonStars()
    {
        for (int i = 0; i < playStars.Length; i++)
        {
            if (playStars[i] == null)
            {
                continue;
            }

            playStarLife[i] = 0.5f;
            float angle = (i / (float)playStars.Length) * 360f + UnityEngine.Random.Range(-25f, 25f);
            float speed = UnityEngine.Random.Range(100f, 200f);
            playStarVel[i] = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad) * speed,
                Mathf.Sin(angle * Mathf.Deg2Rad) * speed);
            playStars[i].anchoredPosition = Vector2.zero;
            playStars[i].sizeDelta = new Vector2(
                UnityEngine.Random.Range(5f, 9f),
                UnityEngine.Random.Range(5f, 9f));
        }
    }

    private void UpdatePlayButtonStars(float dt)
    {
        for (int i = 0; i < playStars.Length; i++)
        {
            if (playStars[i] == null || playStarLife[i] <= 0f)
            {
                continue;
            }

            playStarLife[i] -= dt;
            float alpha = Mathf.Clamp01(playStarLife[i] / 0.5f);
            playStars[i].anchoredPosition += playStarVel[i] * dt;
            playStarVel[i] *= 1f - 2.5f * dt;

            Image img = playStars[i].GetComponent<Image>();
            if (img != null)
            {
                Color c = i % 2 == 0
                    ? new Color(1f, 0.84f, 0.18f, alpha)
                    : new Color(1f, 0.48f, 0.17f, alpha);
                img.color = c;
            }
        }
    }

    private void OnDestroy()
    {
        PlayerProfileService.ProfileChanged -= HandleProfileChanged;
        if (roundedPlateSprite != null)
        {
            Destroy(roundedPlateSprite);
        }
        if (roundedPlateTexture != null)
        {
            Destroy(roundedPlateTexture);
        }
        if (arenaGradientTexture != null)
        {
            Destroy(arenaGradientTexture);
        }
        if (radialGlowSprite != null)
        {
            Destroy(radialGlowSprite);
        }
        if (radialGlowTexture != null)
        {
            Destroy(radialGlowTexture);
        }
    }

    private void BuildMenu()
    {
        GameObject canvasObject = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = menuCamera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        BuildBackdrop(canvas.transform);
        safeRoot = CreateRect("SafeArea", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ApplySafeArea();
        BuildTopBar();
        BuildRobotShowcase();
        BuildCommandDeck();
        BuildDailyStatus();
        BuildFooter();
        RefreshProfilePresentation();
    }

    /// <summary>
    /// The stage the robot stands on.
    ///
    /// The previous backdrop was a boxed "hangar bay" plus mirrored pylons, floor
    /// lanes and cross rails. All of it sat at roughly the same value as the
    /// robot, so the screen read as one flat field of purple with no foreground,
    /// and the bay's hard rectangle cut across the robot's wheels. What replaces
    /// it does one job: darken everything that is not the robot, so the eye has
    /// somewhere to land. Contrast, not decoration, is what gives a lobby depth.
    /// </summary>
    private void BuildBackdrop(Transform parent)
    {
        RawImage stage = CreateRawImage("LiveSkyArenaStage", parent, Color.white,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        previewPresenter = stage.gameObject.AddComponent<RobotPreviewPresenter>();
        previewPresenter.Initialize(robotPreviewPrefab, stage);
        previewPresenter.ConfigureHomePresentation();
    }

    private TextMeshProUGUI levelText, xpText;
    private RectTransform xpFill;

    /// <summary>
    /// Identity on the left, wallet on the right, both compact.
    ///
    /// Three rows in a strict order of importance: the name reads first, the
    /// level second, the XP figure last, and the bar restates the same number
    /// without words. The panel is kept narrow deliberately - the old one ran a
    /// third of the way across the screen to hold four short strings, which made
    /// a status readout look like a feature.
    /// </summary>
    private void BuildTopBar()
    {
        var profile = FrontendUI.Panel("Pilot Profile", safeRoot, new Vector2(.020f, .862f), new Vector2(.248f, .972f), FrontendUI.Ink);
        // Tapping the profile is the optional, non-blocking way to pick a pilot name.
        profile.gameObject.AddComponent<Button>().onClick.AddListener(OpenOptionalNameDialog);
        FrontendUI.Panel("Gold Edge", profile.transform, new Vector2(0, .10f), new Vector2(.011f, .90f), FrontendUI.Gold, false).raycastTarget = false;

        playerNameText = FrontendUI.Text("PilotName", profile.transform, "", new Vector2(.065f, .52f), new Vector2(.96f, .95f), 32, Color.white);
        playerNameText.alignment = TextAlignmentOptions.Left;

        levelText = FrontendUI.Text("Level", profile.transform, "", new Vector2(.065f, .26f), new Vector2(.40f, .52f), 19, FrontendUI.Gold);
        levelText.alignment = TextAlignmentOptions.Left;

        xpText = FrontendUI.Text("XP", profile.transform, "", new Vector2(.42f, .26f), new Vector2(.96f, .52f), 17, new Color(.72f, .82f, .93f));
        xpText.alignment = TextAlignmentOptions.Right;

        var track = FrontendUI.Panel("XP Track", profile.transform, new Vector2(.065f, .10f), new Vector2(.96f, .20f), new Color(.09f, .18f, .32f));
        track.raycastTarget = false;
        xpFill = FrontendUI.Panel("XP Progress", track.transform, Vector2.zero, Vector2.one, FrontendUI.Cyan).rectTransform;

        // Wallet: icon then figure, sized to the number rather than padded out
        // into a panel. There is one currency, so it needs one line.
        Image plate = FrontendUI.Panel("Coins", safeRoot, new Vector2(.845f, .884f), new Vector2(.978f, .968f), FrontendUI.Ink);
        FrontendUI.Rect("CoinIcon", plate.transform, new Vector2(.05f, .16f), new Vector2(.30f, .84f))
            .gameObject.AddComponent<FrontendCoinIcon>().raycastTarget = false;
        coinsText = FrontendUI.Text("CoinValue", plate.transform, "", new Vector2(.34f, .12f), new Vector2(.94f, .88f), 28, Color.white);
        coinsText.alignment = TextAlignmentOptions.Right;
        DailyBonusView.Create(safeRoot);
    }

    // After the command deck, so the MISSIONS button exists for its ready dot.
    private void BuildDailyStatus() => DailyStatusView.Create(safeRoot);

    /// <summary>
    /// The robot and the pad it stands on.
    ///
    /// Raised and enlarged relative to the old layout. Previously the pad sat
    /// below the horizon line so the floor ran straight through the robot's
    /// wheels, and the render was small enough that the flanking buttons carried
    /// as much visual weight as the character the screen is supposed to sell.
    /// </summary>
    private void BuildRobotShowcase()
    {

    }

    /// <summary>
    /// Everything the player can act on.
    ///
    /// The old deck put five buttons of near-identical weight around the robot --
    /// CUSTOMIZE and PRACTICE on the left, CREATE ROOM and JOIN ROOM on the
    /// right, PLAY in the corner -- plus a wide event card that overlapped the
    /// robot's pad. Nothing told the player where to look first.
    ///
    /// This version has exactly one primary action. PLAY moves to bottom centre
    /// and grows; the two room buttons collapse behind a single FRIENDS button
    /// so the right rail stops competing with the left; and the event card
    /// becomes a thin strip above PLAY instead of a block beside it.
    /// </summary>
    /// <summary>
    /// Three tiers, so the screen answers "what do I press" before it is read.
    ///
    /// PLAY is the only primary: warm face, largest area, dead centre. CREATE and
    /// JOIN are secondary blue and symmetric about it, so they read as variants
    /// of the same idea rather than rivals to it. SHOP and MISSIONS drop to a
    /// quiet navy on the rail - they were the same saturated blue and the same
    /// size as the match buttons, which is what made them look like slabs parked
    /// beside the robot.
    /// </summary>
    private void BuildCommandDeck()
    {
        FrontendUI.ActionButton("Shop", safeRoot, "SHOP", new Vector2(.020f, .600f), new Vector2(.146f, .688f),
            FrontendUI.Tier.Tertiary, "Shop", OpenShop, 24f);
        FrontendUI.ActionButton("Missions", safeRoot, "MISSIONS", new Vector2(.020f, .496f), new Vector2(.146f, .584f),
            FrontendUI.Tier.Tertiary, "Missions", OpenMissions, 24f);
        FrontendUI.ActionButton("Loadout", safeRoot, "LOADOUT", new Vector2(.020f, .392f), new Vector2(.146f, .480f),
            FrontendUI.Tier.Tertiary, "Loadout", OpenLoadout, 24f);

        FrontendUI.ActionButton("Create", safeRoot, "CREATE", new Vector2(.215f, .055f), new Vector2(.375f, .155f),
            FrontendUI.Tier.Secondary, "Create", OpenCreateRoom, 27f);
        FrontendUI.ActionButton("PlayOnline", safeRoot, "PLAY", new Vector2(.395f, .035f), new Vector2(.605f, .175f),
            FrontendUI.Tier.Primary, "PlayOnline", RequestQuickMatch, 46f);
        FrontendUI.ActionButton("Join", safeRoot, "JOIN", new Vector2(.625f, .055f), new Vector2(.785f, .155f),
            FrontendUI.Tier.Secondary, "Join", OpenJoinRoom, 27f);
    }

    /// <summary>
    /// Settings, in the corner opposite the primary action.
    ///
    /// The PRIVACY and TERMS links that used to sit beside it were removed at the
    /// owner's request. The document plumbing below is deliberately left in place:
    /// the legal flow is deferred until the real documents exist, not abandoned,
    /// and re-adding two links to this chip is a smaller job than rebuilding it.
    /// </summary>
    private void BuildFooter()
    {
        // The chip is now sized to the single icon it holds. Left at its old width
        // it read as an empty bar with a button pushed into one end.
        Image chip = FrontendUI.Panel("Utility", safeRoot, new Vector2(.930f, .048f), new Vector2(.988f, .122f), FrontendUI.Ink);
        chip.raycastTarget = false;

        FrontendUI.ActionButton("Settings", chip.transform, "", new Vector2(.09f, .10f), new Vector2(.91f, .90f),
            FrontendUI.Tier.Tertiary, "Settings", OpenSettings);
    }

    [Header("Legal")]
    [SerializeField, Tooltip("Full privacy policy text. Shown verbatim by the PRIVACY link.")]
    private TextAsset privacyPolicyDocument;
    [SerializeField, Tooltip("Full terms and conditions text. Shown verbatim by the TERMS link.")]
    private TextAsset termsConditionsDocument;

    private void OpenPrivacyPolicy() => OpenLegalDocument("PRIVACY POLICY", privacyPolicyDocument);
    private void OpenTermsConditions() => OpenLegalDocument("TERMS & CONDITIONS", termsConditionsDocument);

    /// <summary>
    /// Shows an authored legal document, and says so plainly when none is set.
    ///
    /// The text is never generated or paraphrased here: it is whatever asset is
    /// assigned, or an explicit notice that one is missing. Inventing the wording
    /// of a policy the studio has not written would be worse than showing nothing.
    /// </summary>
    private void OpenLegalDocument(string title, TextAsset document)
    {
        string body = document != null && !string.IsNullOrWhiteSpace(document.text)
            ? document.text
            : "THIS DOCUMENT HAS NOT BEEN ADDED TO THE BUILD YET.";
        OpenDialog(title, body, new[] { new DialogAction("CLOSE", panelRaised, CloseDialog) });
    }

    private void OpenMissions()
    {
        MissionPanel.Open(safeRoot);
    }

    private void OpenLoadout()
    {
        LoadoutPanel.Open(safeRoot);
    }

    private void OpenCreateRoom()
    {
        // Both players type the same code. With a Fusion named session whoever
        // arrives first creates it and the second joins, so create and join are
        // the same operation - and pairing is deterministic instead of racing.
        OpenDialog("PRIVATE ROOM", "CHOOSE A CODE AND SHARE IT WITH THE OTHER PLAYER.", new[]
        {
            new DialogAction("CREATE", yellow, RequestCreateRoom),
            new DialogAction("CANCEL", panelRaised, CloseDialog)
        }, true, "DUEL01", "ROOM CODE", 32);
    }

    private void OpenNameDialog()
    {
        if (PlayerProfileService.HasConfirmedName) return;
        choosingName = false;
        OpenDialog("CHOOSE YOUR PILOT NAME", "YOUR NAME APPEARS IN MATCHES. ONCE CONFIRMED, IT CANNOT BE CHANGED.", new[]
        {
            new DialogAction("CONFIRM NAME", lime, SavePlayerName),
            new DialogAction("LATER", panelRaised, CloseDialog)
        }, true, ProfileStore.GetString(PlayerProfileService.PlayerNameKey, string.Empty), "TYPE PILOT NAME", 16);
        choosingName = true;
        nameError = CreateText("NameValidation", dialogInput.transform.parent, "", 17f, FontStyles.Bold, danger,
            new Vector2(.1f,.22f), new Vector2(.9f,.28f), Vector2.zero, Vector2.zero);
        dialogInput.ActivateInputField();
    }

    private void SavePlayerName()
    {
        if (!PlayerProfileService.ValidatePlayerName(dialogInput != null ? dialogInput.text : string.Empty,
            out string candidate, out string error))
        { if (nameError != null) nameError.text = error; return; }
        choosingName = false;
        OpenDialog("USE \"" + candidate + "\"?", "CHOOSE CAREFULLY. YOUR NAME CANNOT BE CHANGED LATER.", new[]
        {
            new DialogAction("CONFIRM", lime, () =>
            {
                if (!PlayerProfileService.TryConfirmPlayerName(candidate, out _)) return;
                choosingName = false;
                CloseDialog();
            }),
            new DialogAction("BACK", panelRaised, OpenNameDialog)
        });
        choosingName = true;
    }

    private void RequestQuickMatch()
    {
        CloseDialog();
        FrontendFlow.Instance.StartOnlineMatchmaking();
    }

    private void RequestCreateRoom()
    {
        string code = dialogInput != null ? dialogInput.text : string.Empty;
        CloseDialog();
        FrontendFlow.Instance.StartPrivateRoom(code, PrivateRoomAction.Create);
    }

    /// <summary>
    /// Create and join live here rather than as two more buttons on the home
    /// screen. Both are low-frequency actions, and giving them permanent screen
    /// weight was a large part of why the lobby read as a control panel.
    /// </summary>


    private void OpenJoinRoom()
    {
        OpenDialog("JOIN ONLINE ROOM", "ENTER THE ROOM CODE PROVIDED BY THE HOST.", new[]
        {
            new DialogAction("JOIN ROOM", cyan, SubmitJoinRoom),
            new DialogAction("CANCEL", panelSoft, CloseDialog)
        }, true, string.Empty, "ROOM CODE", 32);
    }

    private void SubmitJoinRoom()
    {
        string code = dialogInput != null ? dialogInput.text : string.Empty;
        CloseDialog();
        FrontendFlow.Instance.StartPrivateRoom(code, PrivateRoomAction.Join);
    }





    private readonly List<GameObject> hiddenHomeControls = new List<GameObject>();

    private void OpenShop()
    {
        CloseDialog();
        CloseShop();
        foreach (Transform child in safeRoot)
            if (child.gameObject.activeSelf && child.name != "Coins")
            { hiddenHomeControls.Add(child.gameObject); child.gameObject.SetActive(false); }
        shopCategory = RobotCosmeticCategory.Skin;
        selectedCosmetic = PlayerProfileService.GetEquipped(shopCategory);
        previewPresenter.SetShopPresentation(true);
        previewPresenter.PreviewSkin(selectedCosmetic.Id);
        shopOverlay = FrontendUI.Panel("SkinSelector", safeRoot, Vector2.zero, Vector2.one, Color.clear, false).gameObject;
        FrontendUI.Text("ShopTitle", shopOverlay.transform, "SHOP", new Vector2(.4f,.88f), new Vector2(.6f,.97f), 54, Color.white);
        FrontendUI.Button("CloseShop", shopOverlay.transform, "BACK", new Vector2(.025f,.87f), new Vector2(.145f,.95f), FrontendUI.Blue, CloseShop);
        shopItemTitle = FrontendUI.Text("SkinName", shopOverlay.transform, "", new Vector2(.32f,.405f), new Vector2(.68f,.475f), 38, Color.white);
        FrontendUI.Button("PreviousSkin", shopOverlay.transform, "<", new Vector2(.25f,.411f), new Vector2(.30f,.47f), FrontendUI.Blue, () => StepSkin(-1));
        FrontendUI.Button("NextSkin", shopOverlay.transform, ">", new Vector2(.70f,.411f), new Vector2(.75f,.47f), FrontendUI.Blue, () => StepSkin(1));
        shopItemsRoot = FrontendUI.Rect("SkinCards", shopOverlay.transform, new Vector2(.19f,.155f), new Vector2(.81f,.39f));
        shopActionButton = FrontendUI.Button("ShopAction", shopOverlay.transform, "", new Vector2(.39f,.045f), new Vector2(.61f,.13f), FrontendUI.Gold, ExecuteShopAction);
        shopActionLabel = shopActionButton.GetComponentInChildren<TextMeshProUGUI>();
        shopMessage = FrontendUI.Text("ShopMessage", shopOverlay.transform, "", new Vector2(.64f,.055f), new Vector2(.94f,.13f), 21, Color.white);
        RebuildShopCards();
        UpdateShopDetail();
        UIAudioManager.Play(UISoundType.PanelOpen);
    }

    private List<RobotCosmeticDefinition> ShopSkins()
    {
        var items = RobotCosmeticCatalog.GetItems(RobotCosmeticCategory.Skin);
        items.RemoveAll(item => item.Id == "skin_ironclad" || item.Id == "skin_solar" || item.Id == "skin_void");
        return items;
    }

    private void StepSkin(int direction)
    {
        var items = ShopSkins();
        int index = items.FindIndex(item => item.Id == selectedCosmetic.Id);
        SelectCosmetic(items[(index + direction + items.Count) % items.Count]);
    }

    private void RebuildShopCards()
    {
        if (shopItemsRoot == null) return;
        ClearChildren(shopItemsRoot);
        var items = ShopSkins();
        var assets = FrontendAssets.Load();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            bool selected = selectedCosmetic != null && selectedCosmetic.Id == item.Id;
            bool equipped = PlayerProfileService.CurrentEquippedSkinId == item.Id;
            bool owned = PlayerProfileService.IsOwned(item.Id);
            var card = FrontendUI.Panel("Cosmetic_" + item.Id, shopItemsRoot, new Vector2(i / 5f + .007f,.015f), new Vector2((i + 1) / 5f - .007f,.96f), selected ? FrontendUI.Gold : FrontendUI.Ink);
            var face = FrontendUI.Panel("CardFace", card.transform, new Vector2(.018f,.022f), new Vector2(.982f,.98f), selected ? new Color(.10f,.28f,.48f) : new Color(.07f,.15f,.27f));
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            // Skin cards are assembled here rather than through CreateButton or the
            // ReleaseMenuUI factory, so they carry no click sound of their own.
            button.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));
            button.onClick.AddListener(() => SelectCosmetic(item));
            card.gameObject.AddComponent<FrontendPressFeedback>();
            card.transform.localScale = Vector3.one * (selected ? 1.025f : 1);
            var slot = FrontendUI.Rect("ThumbnailSlot", face.transform, new Vector2(.08f,.36f), new Vector2(.92f,.98f));
            var thumbnail = FrontendUI.Rect("SkinThumbnail", slot, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            thumbnail.texture = assets.FindSkin(item.Id)?.Thumbnail;
            thumbnail.raycastTarget = false;
            var aspect = thumbnail.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1;
            FrontendUI.Text("Name", face.transform, item.DisplayName, new Vector2(.035f,.19f), new Vector2(.965f,.34f), 19, Color.white);
            string state = equipped ? "EQUIPPED" : owned ? "OWNED" : item.CoinPrice.ToString("N0");
            if (!owned) FrontendUI.Rect("PriceCoin",face.transform,new Vector2(.08f,.04f),new Vector2(.24f,.17f)).gameObject.AddComponent<FrontendCoinIcon>();
            FrontendUI.Text("State", face.transform, state, new Vector2(.03f,.035f), new Vector2(.97f,.17f), 17, equipped ? FrontendUI.Gold : new Color(.76f,.85f,.94f));
        }
    }

    private void SelectCosmetic(RobotCosmeticDefinition item)
    {
        selectedCosmetic = item;
        previewPresenter.PreviewSkin(item.Id);
        if (shopMessage != null) shopMessage.text = "";
        RebuildShopCards();
        UpdateShopDetail();
    }

    private void UpdateShopDetail()
    {
        if (selectedCosmetic == null || shopActionButton == null) return;
        shopItemTitle.text = selectedCosmetic.DisplayName;
        bool owned = PlayerProfileService.IsOwned(selectedCosmetic.Id);
        bool equipped = PlayerProfileService.CurrentEquippedSkinId == selectedCosmetic.Id;
        shopActionLabel.text = equipped ? "EQUIPPED" : owned ? "EQUIP" : "BUY  " + selectedCosmetic.CoinPrice.ToString("N0");
        shopActionButton.interactable = !equipped;
    }

    private void ExecuteShopAction()
    {
        if (selectedCosmetic == null) return;
        string message;
        if (!PlayerProfileService.IsOwned(selectedCosmetic.Id))
        {
            PlayerProfileService.TryPurchase(selectedCosmetic.Id, out message);
        }
        else PlayerProfileService.TryEquip(selectedCosmetic.Id, out message);
        shopMessage.text = message;
        RebuildShopCards();
        UpdateShopDetail();
    }

    private void CloseShop()
    {
        if (shopOverlay != null) { shopOverlay.SetActive(false); Destroy(shopOverlay); shopOverlay = null; }
        foreach (var control in hiddenHomeControls) if (control != null) control.SetActive(true);
        hiddenHomeControls.Clear();
        if (previewPresenter != null) previewPresenter.SetShopPresentation(false);
        shopCanvasGroup = null; shopTabsRoot = null; shopItemsRoot = null;
        shopItemTitle = null; shopItemDescription = null; shopMessage = null;
        shopActionButton = null; shopActionLabel = null;
    }

    private void HandleProfileChanged()
    {
        RefreshProfilePresentation();
        if (shopOverlay != null)
        {
            UpdateShopDetail();
        }
    }

    private void RefreshProfilePresentation()
    {
        if (playerNameText != null)
        {
            // DisplayName: the same name opponents see (the CrazyGames username when
            // the player is logged in, otherwise the pilot or generated guest name).
            playerNameText.text = PlayerProfileService.DisplayName;
        }
        if (coinsText != null)
        {
            string value = PlayerProfileService.Coins.ToString("N0");
            if (coinsText.text.Length > 0 && coinsText.text != value)
                StartCoroutine(PulseCoins(coinsText.transform));
            coinsText.text = value;
        }

        if (levelText != null) levelText.text = "LV. " + PlayerProfileService.Level;
        if (xpText != null) xpText.text = PlayerProfileService.Experience + " / " + PlayerProfileService.ExperienceToNextLevel + " XP";
        if (xpFill != null) xpFill.anchorMax = new Vector2(Mathf.Clamp01((float)PlayerProfileService.Experience / PlayerProfileService.ExperienceToNextLevel),1);
        RefreshOnlineState();
    }

    private IEnumerator PulseCoins(Transform label)
    {
        float elapsed = 0;
        while (elapsed < .2f && label != null)
        {
            elapsed += Time.unscaledDeltaTime;
            label.localScale = Vector3.one * (1 + Mathf.Sin(Mathf.Clamp01(elapsed / .2f) * Mathf.PI) * .06f);
            yield return null;
        }
        if (label != null) label.localScale = Vector3.one;
    }

    private void RefreshOnlineState()
    {
        if (onlineStateText == null)
        {
            return;
        }

        onlineStateText.text = OnlineSessionService.IsConfigured
            ? "SERVER ONLINE"
            : "ONLINE PREVIEW";
        onlineStateText.color = OnlineSessionService.IsConfigured ? lime : Color.white;
    }



    private void OpenDialog(string title, string message, DialogAction[] actions, bool includeInput = false,
        string inputValue = "", string placeholder = "", int characterLimit = 16)
    {
        CloseShop();
        CloseDialog();
        dialog = CreateImage("ModalOverlay", safeRoot, new Color(0.006f, 0.015f, 0.035f, 0.94f),
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true);
        GameObject window = CreatePanel("ModalWindow", dialog.transform, panel,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(900f, includeInput ? 555f : 490f), cyan, 3f);
        CreateImage("ModalHeader", window.transform, panelRaised, new Vector2(0f, 0.79f), Vector2.one,
            Vector2.zero, Vector2.zero);
        CreateText("ModalTitle", window.transform, title, 37f, FontStyles.Bold, Color.white,
            new Vector2(0.5f, 0.895f), new Vector2(0.5f, 0.895f), Vector2.zero,
            new Vector2(760f, 62f));
        TextMeshProUGUI body = CreateText("ModalMessage", window.transform, message, 19f,
            FontStyles.Normal, Color.white, new Vector2(0.08f, includeInput ? 0.48f : 0.42f),
            new Vector2(0.92f, 0.72f), Vector2.zero, Vector2.zero, TextAlignmentOptions.Center, true);
        body.lineSpacing = 8f;

        dialogInput = null;
        if (includeInput)
        {
            dialogInput = CreateInput("ModalInput", window.transform, inputValue, placeholder,
                new Vector2(0.5f, 0.38f), Vector2.zero, new Vector2(620f, 72f), characterLimit);
        }

        float spacing = Mathf.Min(300f, 760f / Mathf.Max(1, actions.Length));
        float startX = -(actions.Length - 1) * spacing * 0.5f;
        for (int index = 0; index < actions.Length; index++)
        {
            DialogAction action = actions[index];
            Button button = CreateButton("ModalAction_" + index, window.transform, action.Label,
                action.Color, Color.white, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(startX + index * spacing, 64f), new Vector2(spacing - 22f, 78f), 20f);
            button.onClick.AddListener(action.Callback);
        }

        // Brawl Stars-style bounce-in transition for dialogs.
        dialogCanvasGroup = dialog.AddComponent<CanvasGroup>();
        dialogCanvasGroup.alpha = 0f;
        UITweenEngine.FadeCanvasGroup(dialogCanvasGroup, 0f, 1f, 0.12f);
        UITweenEngine.BounceIn(window.transform, 0.25f);
        UIAudioManager.Play(UISoundType.PanelOpen);
    }

    private void CloseDialog()
    {
        choosingName = false;
        if (dialog != null) { dialog.SetActive(false); Destroy(dialog); dialog = null; }
        dialogCanvasGroup = null;
        dialogInput = null;
    }

    private TMP_InputField CreateInput(string objectName, Transform parent, string value, string placeholderText,
        Vector2 anchor, Vector2 position, Vector2 size, int characterLimit)
    {
        GameObject inputObject = CreatePanel(objectName, parent, new Color(0.018f, 0.055f, 0.115f, 1f),
            anchor, anchor, position, size, cyanDark, 2f);
        Image inputImage = inputObject.GetComponent<Image>();
        inputImage.raycastTarget = true;
        TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
        input.targetGraphic = inputImage;

        RectTransform textArea = CreateRect("Text Area", inputObject.transform, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero);
        textArea.offsetMin = new Vector2(24f, 8f);
        textArea.offsetMax = new Vector2(-24f, -8f);
        TextMeshProUGUI text = CreateText("Text", textArea, value, 25f, FontStyles.Bold, Color.white,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
        TextMeshProUGUI placeholderLabel = CreateText("Placeholder", textArea, placeholderText, 22f,
            FontStyles.Bold, muted, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Left);
        input.textViewport = textArea;
        input.textComponent = text;
        input.placeholder = placeholderLabel;
        input.text = value;
        input.characterLimit = characterLimit;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    private Button CreateButton(string objectName, Transform parent, string label, Color background, Color textColor,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, float fontSize)
    {
        // 3D depth bottom edge — darker strip beneath the button face.
        Color depthColor = new Color(background.r * 0.55f, background.g * 0.55f, background.b * 0.55f, background.a);
        GameObject depthStrip = CreateImage(objectName + "_Depth", parent, depthColor,
            anchorMin, anchorMax, position + new Vector2(0f, -3f), new Vector2(size.x, size.y * 0.25f), false);
        Image depthImage = depthStrip.GetComponent<Image>();
        depthImage.sprite = GetRoundedPlateSprite();
        depthImage.type = Image.Type.Sliced;

        GameObject buttonObject = CreateImage(objectName, parent, background, anchorMin, anchorMax, position, size, true);
        Image image = buttonObject.GetComponent<Image>();
        image.sprite = GetRoundedPlateSprite();
        image.type = Image.Type.Sliced;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
        colors.pressedColor = new Color(0.72f, 0.80f, 0.9f, 1f);
        colors.disabledColor = new Color(0.38f, 0.44f, 0.52f, 0.75f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        TextMeshProUGUI text = CreateText("Label", buttonObject.transform, label, fontSize,
            FontStyles.Bold, textColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ApplyTextStroke(text, ink, 0.12f);
        text.raycastTarget = false;

        // Audio hooks — chunky click on press.
        button.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));

        // Pointer down/up press effect — button shifts down 3px on press, snaps back on release.
        EventTrigger trigger = buttonObject.AddComponent<EventTrigger>();
        RectTransform btnRect = buttonObject.GetComponent<RectTransform>();
        Vector2 restPos = position;

        EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDown.callback.AddListener((_) => { if (btnRect != null) btnRect.anchoredPosition = restPos + new Vector2(0f, -3f); });
        trigger.triggers.Add(pointerDown);

        EventTrigger.Entry pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUp.callback.AddListener((_) => { if (btnRect != null) btnRect.anchoredPosition = restPos; });
        trigger.triggers.Add(pointerUp);

        return button;
    }

    private Button CreateHomeButton(string objectName, Transform parent, string label, Color background,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, float fontSize,
        Color? textColor = null)
    {
        GameObject shadow = CreateImage(objectName + "_Shadow", parent, new Color(0.05f, 0.035f, 0.12f, 0.78f),
            anchorMin, anchorMax, position + new Vector2(0f, -7f), size);
        Image shadowImage = shadow.GetComponent<Image>();
        shadowImage.sprite = GetRoundedPlateSprite();
        shadowImage.type = Image.Type.Sliced;

        Button button = CreateButton(objectName, parent, label, background, textColor ?? Color.white,
            anchorMin, anchorMax, position, size, fontSize);
        UnityEngine.UI.Outline outline = button.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = ink;
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = true;

        // Gloss. It has to be rounded and faint: a hard-edged rectangle at high
        // alpha reads as a progress bar sitting on the button, and a sliver
        // pinned to the top edge reads as a stray line above it. A rounded plate
        // at low alpha, inset on all sides, is the only version that reads as
        // shine on the button's own surface.
        GameObject highlight = CreateImage("Highlight", button.transform, new Color(1f, 1f, 1f, 0.09f),
            new Vector2(0.06f, 0.56f), new Vector2(0.94f, 0.88f), Vector2.zero, Vector2.zero);
        Image highlightImage = highlight.GetComponent<Image>();
        highlightImage.sprite = GetRoundedPlateSprite();
        highlightImage.type = Image.Type.Sliced;
        TextMeshProUGUI labelText = button.GetComponentInChildren<TextMeshProUGUI>();
        if (labelText != null)
        {
            labelText.rectTransform.anchoredPosition = new Vector2(0f, 7f);
        }
        return button;
    }

    private void AddButtonAccent(Transform button, Color color, string caption)
    {
        TextMeshProUGUI subLabel = CreateText("SubLabel", button, caption, 13f, FontStyles.Bold, color,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f),
            new Vector2(300f, 24f));
        ApplyTextStroke(subLabel, ink, 0.07f);
    }

    private GameObject CreatePanel(string objectName, Transform parent, Color color, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 position, Vector2 size, Color outlineColor, float outlineSize)
    {
        GameObject panelObject = CreateImage(objectName, parent, color, anchorMin, anchorMax, position, size);
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.sprite = GetRoundedPlateSprite();
        panelImage.type = Image.Type.Sliced;
        UnityEngine.UI.Outline outline = panelObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(outlineSize, -outlineSize);
        outline.useGraphicAlpha = true;
        return panelObject;
    }

    private Texture2D GetArenaGradientTexture()
    {
        if (arenaGradientTexture != null)
        {
            return arenaGradientTexture;
        }

        const int width = 16;
        const int height = 256;
        arenaGradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "RoboMania_ArenaGradient_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Color bottom = Hex("#322750");
        Color middle = Hex("#7146A2");
        Color top = Hex("#4E4A91");
        for (int y = 0; y < height; y++)
        {
            float vertical = y / (height - 1f);
            Color row = vertical < 0.55f
                ? Color.Lerp(bottom, middle, vertical / 0.55f)
                : Color.Lerp(middle, top, (vertical - 0.55f) / 0.45f);
            for (int x = 0; x < width; x++)
            {
                float centreGlow = 1f - Mathf.Abs(x / (width - 1f) * 2f - 1f);
                Color pixel = Color.Lerp(row, Hex("#B66AD0"), centreGlow * 0.14f);
                pixel.a = 1f;
                arenaGradientTexture.SetPixel(x, y, pixel);
            }
        }
        arenaGradientTexture.Apply(false, true);
        return arenaGradientTexture;
    }

    private Sprite GetRoundedPlateSprite()
    {
        if (roundedPlateSprite != null)
        {
            return roundedPlateSprite;
        }

        const int textureSize = 64;
        const float radius = 13f;
        roundedPlateTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "RoboMania_RoundedPlate_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Vector2 centre = new Vector2((textureSize - 1f) * 0.5f, (textureSize - 1f) * 0.5f);
        Vector2 halfInner = new Vector2(textureSize * 0.5f - radius, textureSize * 0.5f - radius);
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                Vector2 q = new Vector2(Mathf.Abs(x - centre.x), Mathf.Abs(y - centre.y)) - halfInner;
                Vector2 outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
                float signedDistance = outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float alpha = Mathf.Clamp01(0.75f - signedDistance);
                float bevel = Mathf.Lerp(0.88f, 1f, y / (textureSize - 1f));
                roundedPlateTexture.SetPixel(x, y, new Color(bevel, bevel, bevel, alpha));
            }
        }
        roundedPlateTexture.Apply(false, false);
        roundedPlateSprite = Sprite.Create(roundedPlateTexture,
            new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(18f, 18f, 18f, 18f));
        roundedPlateSprite.name = "RoboMania_RoundedPlate_Runtime";
        roundedPlateSprite.hideFlags = HideFlags.DontSave;
        return roundedPlateSprite;
    }

    private Sprite GetRadialGlowSprite()
    {
        if (radialGlowSprite != null)
        {
            return radialGlowSprite;
        }

        const int textureSize = 128;
        radialGlowTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "RoboMania_RadialGlow_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        Vector2 centre = new Vector2((textureSize - 1f) * 0.5f, (textureSize - 1f) * 0.5f);
        float radius = textureSize * 0.5f - 1f;
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), centre) / radius;
                float alpha = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((distance - 0.38f) / 0.62f));
                radialGlowTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        radialGlowTexture.Apply(false, true);
        radialGlowSprite = Sprite.Create(radialGlowTexture,
            new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f);
        radialGlowSprite.name = "RoboMania_RadialGlow_Runtime";
        radialGlowSprite.hideFlags = HideFlags.DontSave;
        return radialGlowSprite;
    }

    /// <summary>
    /// Inverse of the radial glow: clear through the middle, opaque at the
    /// corners. Laid over the whole backdrop it pushes the frame edges down in
    /// value so the robot and the primary button are the brightest things on
    /// screen. Stretched to the full canvas, so it is generated as an ellipse
    /// in UV space rather than a circle.
    /// </summary>
    private Sprite GetVignetteSprite()
    {
        if (vignetteSprite != null)
        {
            return vignetteSprite;
        }

        const int textureSize = 128;
        vignetteTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "RoboMania_Vignette_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        Vector2 centre = new Vector2((textureSize - 1f) * 0.5f, (textureSize - 1f) * 0.5f);
        float radius = textureSize * 0.5f;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), centre) / radius;

                // Fully clear until well past the centre, then ramp up quickly,
                // so the darkening reads as framing rather than as a grey wash.
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - 0.48f) / 0.52f));
                vignetteTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        vignetteTexture.Apply(false, true);
        vignetteSprite = Sprite.Create(vignetteTexture,
            new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f);
        vignetteSprite.name = "RoboMania_Vignette_Runtime";
        vignetteSprite.hideFlags = HideFlags.DontSave;
        return vignetteSprite;
    }

    /// <summary>
    /// Generates a tiling 128x128 texture with 45-degree diagonal stripes.
    /// Used for the Brawl Stars-style parallax background overlay.
    /// </summary>
    private Texture2D GetDiagonalStripeTexture()
    {
        if (diagonalStripeTexture != null)
        {
            return diagonalStripeTexture;
        }

        const int size = 128;
        const float stripeWidth = 20f;
        diagonalStripeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RoboMania_DiagonalStripes_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.DontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float diagonal = (x + y) % (stripeWidth * 2f);
                float alpha = diagonal < stripeWidth ? 0.3f : 0f;
                diagonalStripeTexture.SetPixel(x, y, new Color(0.6f, 0.5f, 0.9f, alpha));
            }
        }

        diagonalStripeTexture.Apply(false, true);
        return diagonalStripeTexture;
    }

    private static void ApplyTextStroke(TextMeshProUGUI text, Color color, float width)
    {
        if (text == null)
        {
            return;
        }
        text.outlineColor = color;
        text.outlineWidth = Mathf.Clamp01(width);
    }

    private GameObject CreateImage(string objectName, Transform parent, Color color, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 position, Vector2 size, bool raycastTarget = false)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        SetRect(image.rectTransform, anchorMin, anchorMax, position, size);
        return imageObject;
    }

    private RawImage CreateRawImage(string objectName, Transform parent, Color color, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageObject.transform.SetParent(parent, false);
        RawImage image = imageObject.GetComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        SetRect(image.rectTransform, anchorMin, anchorMax, position, size);
        return image;
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, string content, float fontSize,
        FontStyles style, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center, bool wrapping = false)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = wrapping;
        text.overflowMode = wrapping ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        SetRect(text.rectTransform, anchorMin, anchorMax, position, size);
        return text;
    }

    private RectTransform CreateRect(string objectName, Transform parent, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        GameObject rectObject = new GameObject(objectName, typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        RectTransform rect = rectObject.GetComponent<RectTransform>();
        SetRect(rect, anchorMin, anchorMax, position, size);
        return rect;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void ApplySafeArea()
    {
        Rect area = Screen.safeArea;
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        safeRoot.anchorMin = new Vector2(area.xMin / width, area.yMin / height);
        safeRoot.anchorMax = new Vector2(area.xMax / width, area.yMax / height);
        safeRoot.offsetMin = Vector2.zero;
        safeRoot.offsetMax = Vector2.zero;
        lastSafeArea = area;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
    }

    private void EnsureMenuCamera()
    {
        menuCamera = Camera.main;
        if (menuCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Menu Camera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            menuCamera = cameraObject.GetComponent<Camera>();
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.backgroundColor = midnight;
            menuCamera.cullingMask = 1 << 0;
            menuCamera.nearClipPlane = 0.01f;
            menuCamera.farClipPlane = 10f;
            menuCamera.depth = -100f;
        }

        // UIAudioManager is intentionally global, so the lobby must always
        // provide one listener before it plays button and menu feedback.
        if (FindFirstObjectByType<AudioListener>() == null)
        {
            menuCamera.gameObject.AddComponent<AudioListener>();
        }
    }

    private void CreateEventSystemIfNeeded()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystem.transform.SetParent(transform, false);
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int index = root.childCount - 1; index >= 0; index--)
        {
            GameObject child = root.GetChild(index).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    private static string GetCategoryLabel(RobotCosmeticCategory category)
    {
        switch (category)
        {
            case RobotCosmeticCategory.Skin:
                return "FRAME SKIN";
            case RobotCosmeticCategory.ColorCombo:
                return "COLOR COMBO";
            case RobotCosmeticCategory.EyeShape:
                return "EYE OPTICS";
            case RobotCosmeticCategory.ScreenEffect:
                return "SCREEN FX";
            default:
                return category.ToString().ToUpperInvariant();
        }
    }

    private static Color Hex(string value)
    {
        return ColorUtility.TryParseHtmlString(value, out Color result) ? result : Color.white;
    }


    // ─── Settings Panel ──────────────────────────────────────────────────
    private void OpenSettings()
    {
        CloseDialog();
        CloseSettings();
        settingsOverlay = GameSettingsPanel.Open(safeRoot, CloseSettings);
    }

    private void CloseSettings()
    {
        if (settingsOverlay != null)
        {
            UIAudioManager.Play(UISoundType.PanelClose);
            Destroy(settingsOverlay);
            settingsOverlay = null;
        }
    }

    private readonly struct DialogAction
    {
        public string Label { get; }
        public Color Color { get; }
        public UnityEngine.Events.UnityAction Callback { get; }

        public DialogAction(string label, Color color, UnityEngine.Events.UnityAction callback)
        {
            Label = label;
            Color = color;
            Callback = callback;
        }
    }

}
