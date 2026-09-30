using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The private room screen: room code, both players with live robots, and Start.
///
/// The left side is always this device's player and the right side is always the
/// opponent, on every device. That is presentation only - it does not change who
/// owns which team. Binding a side to a fixed PlayerRef would show one of the two
/// players their opponent where they expect themselves.
/// </summary>
[DisallowMultipleComponent]
public sealed class PrivateLobbyScreen : MonoBehaviour
{
    private RectTransform root;
    private TextMeshProUGUI roomCodeText;
    private TextMeshProUGUI countText;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI localName;
    private TextMeshProUGUI localSkin;
    private TextMeshProUGUI remoteName;
    private TextMeshProUGUI remoteSkin;
    private GameObject remoteWaiting;
    private GameObject remoteCard;
    private Button startButton;
    private TextMeshProUGUI startLabel;
    private RobotPreviewPresenter localBot;
    private RobotPreviewPresenter remoteBot;
    private string shownRemoteSkin;
    private string shownLocalSkin;
    private System.Action onLeave;

    private static readonly Color CardFace = new Color(.07f, .16f, .36f);
    private const float OutlinePixels = 3.5f;
    private int shownPlayers = -1;
    private string shownLocalName, shownLocalLine, shownRemoteName, shownRemoteLine;

    public void Build(RectTransform parent, string roomCode, System.Action leaveAction)
    {
        onLeave = leaveAction;
        root = FrontendUI.Rect("PrivateLobby", parent, Vector2.zero, Vector2.one);

        FrontendBackdrop.Create(root, "LobbyBackdrop");

        roomCodeText = FrontendUI.Text("RoomCode", root, "ROOM CODE: " + roomCode,
            new Vector2(.10f, .855f), new Vector2(.90f, .955f), 60, FrontendUI.Gold);
        roomCodeText.alignment = TextAlignmentOptions.Center;

        BuildPlayerCard(true, new Vector2(.06f, .30f), new Vector2(.475f, .82f));
        BuildPlayerCard(false, new Vector2(.525f, .30f), new Vector2(.94f, .82f));

        countText = FrontendUI.Text("PlayerCount", root, "PLAYERS 1 / 2",
            new Vector2(.10f, .205f), new Vector2(.90f, .275f), 42, Color.white);
        countText.alignment = TextAlignmentOptions.Center;

        statusText = FrontendUI.Text("LobbyStatus", root, "",
            new Vector2(.10f, .135f), new Vector2(.90f, .200f), 34, new Color(.72f, .82f, 1f));
        statusText.alignment = TextAlignmentOptions.Center;

        startButton = FrontendUI.Button("StartMatch", root, "START",
            new Vector2(.355f, .035f), new Vector2(.645f, .125f), FrontendUI.Gold, HandleStart);
        startLabel = startButton.GetComponentInChildren<TextMeshProUGUI>();

        FrontendUI.Button("LeaveRoom", root, "LEAVE",
            new Vector2(.035f, .035f), new Vector2(.215f, .115f), FrontendUI.Blue,
            () => onLeave?.Invoke());

        // Second way to invite, alongside the room code. Only on the portal, where
        // a link means something - off-platform there is nothing to link to.
        inviteRoomCode = roomCode;
        if (CrazyGamesPlatformService.Ready)
        {
            inviteButton = FrontendUI.Button("CopyInviteLink", root, "INVITE LINK",
                new Vector2(.785f, .035f), new Vector2(.965f, .115f), FrontendUI.Blue, CopyInviteLink);
            inviteLabel = inviteButton.GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    private string inviteRoomCode;
    private Button inviteButton;
    private TextMeshProUGUI inviteLabel;

    private void CopyInviteLink()
    {
        bool copied = CrazyGamesPlatformService.CopyInviteLink(inviteRoomCode);
        if (inviteLabel == null) return;
        StopAllCoroutines();
        StartCoroutine(FlashInviteLabel(copied ? "LINK COPIED" : "LINK UNAVAILABLE"));
    }

    /// <summary>Brief inline confirmation on the button itself, rather than a
    /// popup the player then has to dismiss.</summary>
    private System.Collections.IEnumerator FlashInviteLabel(string message)
    {
        inviteLabel.text = message;
        yield return new WaitForSecondsRealtime(1.5f);
        if (inviteLabel != null) inviteLabel.text = "INVITE LINK";
    }

    private void BuildPlayerCard(bool local, Vector2 min, Vector2 max)
    {
        // Solid card: team-accent frame, navy face, a lit stage behind the robot.
        Image frame = FrontendUI.Panel(local ? "YouCard" : "OpponentCard", root, min, max, local ? FrontendUI.Gold : FrontendUI.Cyan);
        frame.raycastTarget = false;
        Image panel = FrontendUI.Inset("Face", frame.transform, CardFace, 6f, 6f, 6f, 6f);
        panel.raycastTarget = false;
        Transform parent = panel.transform;
        FrontendBackdrop.Stage(parent, new Vector2(.05f, .29f), new Vector2(.95f, .86f));

        TextMeshProUGUI heading = FrontendUI.Text("Heading", parent, local ? "YOU" : "OPPONENT",
            new Vector2(.05f, .86f), new Vector2(.95f, .98f), 34,
            local ? FrontendUI.Gold : new Color(.78f, .86f, 1f));
        heading.alignment = TextAlignmentOptions.Center;

        var stage = FrontendUI.Rect("BotStage", parent, new Vector2(.08f, .30f), new Vector2(.92f, .86f))
            .gameObject.AddComponent<RawImage>();
        stage.raycastTarget = false;

        var presenter = stage.gameObject.AddComponent<RobotPreviewPresenter>();
        if (local && presenter.Initialize(null, stage, false)) presenter.EnableSilhouetteOutline(OutlinePixels);

        TextMeshProUGUI nameText = FrontendUI.Text("Name", parent, "",
            new Vector2(.05f, .16f), new Vector2(.95f, .28f), 36, Color.white);
        nameText.alignment = TextAlignmentOptions.Center;

        TextMeshProUGUI skinText = FrontendUI.Text("Skin", parent, "",
            new Vector2(.05f, .05f), new Vector2(.95f, .15f), 26, new Color(.66f, .76f, .95f));
        skinText.alignment = TextAlignmentOptions.Center;

        if (local)
        {
            localBot = presenter;
            localName = nameText;
            localSkin = skinText;
        }
        else
        {
            remoteBot = presenter;
            remoteName = nameText;
            remoteSkin = skinText;
            remoteCard = stage.gameObject;

            var waiting = FrontendUI.Text("Waiting", parent, "WAITING FOR\nOPPONENT...",
                new Vector2(.05f, .32f), new Vector2(.95f, .80f), 34, new Color(.62f, .72f, .92f));
            waiting.alignment = TextAlignmentOptions.Center;
            remoteWaiting = waiting.gameObject;
        }
    }

    private void HandleStart()
    {
        PrivateLobbySession session = PrivateLobbySession.Instance;
        if (session == null)
        {
            Debug.LogWarning("[PRIVATE LOBBY] Start pressed with no lobby session.");
            return;
        }

        // Re-validated here as well as for the button state, because a player can
        // leave between the frame the button lit up and the frame it was pressed.
        if (!session.CanStart(out string reason))
        {
            Debug.LogWarning($"[PRIVATE LOBBY] Start refused on click: {reason}.");
            return;
        }

        session.RequestStart();
    }

    private void Update()
    {
        PrivateLobbySession session = PrivateLobbySession.Instance;
        PrivateLobbyDirector director = PrivateLobbyDirector.Instance;
        if (director == null) return;

        int players = director.CountPlayers();
        NetworkedLobbyPlayer local = NetworkedLobbyPlayer.Local;
        NetworkedLobbyPlayer remote = NetworkedLobbyPlayer.Remote;

        if (countText != null && players != shownPlayers)
        {
            shownPlayers = players;
            countText.text = $"PLAYERS {players} / {PrivateRoomConnector.RoomSize}";
        }

        UpdateLocalCard(local);
        UpdateRemoteCard(remote);
        UpdateStartControl(session, players);
    }

    private void UpdateLocalCard(NetworkedLobbyPlayer local)
    {
        if (localName == null) return;

        string name = local != null && local.HasIdentity
            ? local.DisplayName.Value
            : PlayerProfileService.PlayerName;

        if (name != shownLocalName) { shownLocalName = name; localName.text = name; }

        string skinId = local != null && local.HasIdentity
            ? local.SkinId.Value
            : PlayerProfileService.CurrentEquippedSkinId;

        // Just the skin: the card heading already says whose robot this is. Sides are
        // not settled in the lobby, so a side-derived "[YOU]" tag read wrong on the
        // opponent's card.
        if (skinId != shownLocalLine)
        {
            shownLocalLine = skinId;
            localSkin.text = PrettySkin(skinId);
        }

        // Driven from the published record rather than the profile, so what this
        // player sees of themselves is exactly what the opponent was told.
        if (localBot != null && skinId != shownLocalSkin)
        {
            shownLocalSkin = skinId;
            localBot.PreviewSkin(skinId);
        }
    }

    private void UpdateRemoteCard(NetworkedLobbyPlayer remote)
    {
        bool present = remote != null && remote.HasIdentity && remote.Owner.IsRealPlayer &&
            remote.Runner != null && remote.Owner != remote.Runner.LocalPlayer;

        if (remoteWaiting != null) remoteWaiting.SetActive(!present);
        if (remoteCard != null) remoteCard.SetActive(present);
        if (remoteName != null) remoteName.gameObject.SetActive(present);
        if (remoteSkin != null) remoteSkin.gameObject.SetActive(present);

        if (!present)
        {
            shownRemoteSkin = null;
            shownRemoteLine = null;
            return;
        }

        string remoteDisplay = remote.DisplayName.Value;
        if (remoteDisplay != shownRemoteName) { shownRemoteName = remoteDisplay; remoteName.text = remoteDisplay; }
        if (remote.SkinId.Value != shownRemoteSkin || shownRemoteLine == null)
        {
            shownRemoteLine = PrettySkin(remote.SkinId.Value);
            remoteSkin.text = shownRemoteLine;
        }

        // Only re-dress the preview when the id actually changes; ApplySkin rebuilds
        // the robot's cosmetics and is not something to run every frame.
        string incoming = remote.SkinId.Value;
        if (remoteBot != null && incoming != shownRemoteSkin)
        {
            if (string.IsNullOrEmpty(remoteBot.DisplayedSkin) &&
                remoteBot.Initialize(null, remoteCard.GetComponent<RawImage>(), false))
                remoteBot.EnableSilhouetteOutline(OutlinePixels);
            shownRemoteSkin = incoming;
            remoteBot.PreviewSkin(incoming);
            Debug.Log($"[LOBBY PROFILE] Opponent preview dressed as {incoming}.");
        }
    }

    private void UpdateStartControl(PrivateLobbySession session, int players)
    {
        if (startButton == null) return;

        if (session == null)
        {
            startButton.gameObject.SetActive(false);
            if (statusText != null) statusText.text = "CONNECTING...";
            return;
        }

        if (session.State == LobbyState.Countdown)
        {
            startButton.gameObject.SetActive(false);
            float remaining = session.CountdownRemaining;
            int whole = Mathf.CeilToInt(remaining);
            statusText.text = whole > 0
                ? $"MATCH STARTING IN\n{whole}"
                : "GO!";
            return;
        }

        if (session.State == LobbyState.LoadingGame)
        {
            startButton.gameObject.SetActive(false);
            statusText.text = "LOADING ARENA...";
            return;
        }

        bool isCreator = session.IsLocalPlayerCreator;
        startButton.gameObject.SetActive(isCreator);

        if (!isCreator)
        {
            statusText.text = players >= 2 ? "WAITING FOR HOST..." : "WAITING FOR OPPONENT...";
            return;
        }

        bool ready = session.CanStart(out string reason);
        startButton.interactable = ready;
        if (startLabel != null)
        {
            startLabel.color = ready ? new Color(.08f, .08f, .12f) : new Color(.45f, .45f, .5f);
        }

        statusText.text = ready ? "" : (players < 2 ? "WAITING FOR OPPONENT..." : reason?.ToUpperInvariant() ?? "");
    }


    private static string PrettySkin(string skinId)
    {
        if (string.IsNullOrEmpty(skinId)) return "";
        var item = RobotCosmeticCatalog.Find(skinId);
        if (item != null && !string.IsNullOrEmpty(item.DisplayName)) return item.DisplayName.ToUpperInvariant();
        string trimmed = skinId.StartsWith("spark_") ? skinId.Substring(6) : skinId.StartsWith("skin_") ? skinId.Substring(5) : skinId;
        return trimmed.ToUpperInvariant();
    }
}
