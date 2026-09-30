using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live round-trip time, top right, whenever the player is in play: the private
/// lobby, the QuickPlay search, online matches, and bot matches.
///
/// A browser game cannot use UDP - Fusion falls back to WebSocket over TCP - so
/// online play carries more latency than a native build ever would, and some of
/// it is the player's own connection. Without a number on screen every bit of
/// that reads as "the game is laggy". Showing the RTT, and colouring it, moves
/// the question from "this game is broken" to "my connection is poor right now".
///
/// The number is always a real measurement of the runner in use (search runner,
/// match runner or lobby runner). With no network session - a bot match, or a
/// search that has not connected yet - it reads N/A instead of a stale or
/// invented figure. Sampled four times a second; the label string is only
/// rebuilt when the rounded value changes.
///
/// Self-contained on purpose: it builds its own overlay canvas rather than
/// hunting for a HUD root, so nothing else has to know it exists and it cannot
/// break a layout it is not part of.
/// </summary>
[DefaultExecutionOrder(500)]
public sealed class NetworkPingDisplay : MonoBehaviour
{
    // Thresholds chosen for a browser game on WebSocket, where a good connection
    // sits well above what a native UDP title would call good.
    private const float GoodMilliseconds = 90f;
    private const float FairMilliseconds = 180f;

    private const float SampleInterval = 0.25f;

    private static readonly Color Good = new Color(0.44f, 0.85f, 0.45f);
    private static readonly Color Fair = new Color(0.98f, 0.78f, 0.27f);
    private static readonly Color Poor = new Color(0.95f, 0.35f, 0.32f);

    private TextMeshProUGUI label;
    private Image dot;
    private CanvasGroup group;
    private float smoothed = -1f;
    private float nextSample;
    private int shownValue = int.MinValue;
    private static readonly Color Unavailable = new Color(0.62f, 0.68f, 0.78f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var host = new GameObject("Network Ping");
        DontDestroyOnLoad(host);
        host.AddComponent<NetworkPingDisplay>();
    }

    private void Awake()
    {
        var canvasObject = new GameObject("PingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the gameplay HUD so a full-screen panel cannot bury it.
        canvas.sortingOrder = 5000;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Nothing here should ever eat a click meant for the game.
        canvasObject.GetComponent<GraphicRaycaster>().enabled = false;

        var plate = new GameObject("Plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        plate.transform.SetParent(canvasObject.transform, false);
        var plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = plateRect.anchorMax = new Vector2(1f, 1f);
        plateRect.pivot = new Vector2(1f, 1f);
        plateRect.anchoredPosition = new Vector2(-18f, -18f);
        plateRect.sizeDelta = new Vector2(196f, 44f);
        var plateImage = plate.GetComponent<Image>();
        plateImage.color = new Color(0.045f, 0.09f, 0.20f, 0.92f);
        plateImage.raycastTarget = false;

        var dotObject = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dotObject.transform.SetParent(plate.transform, false);
        var dotRect = (RectTransform)dotObject.transform;
        dotRect.anchorMin = dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0f, 0.5f);
        dotRect.anchoredPosition = new Vector2(12f, 0f);
        dotRect.sizeDelta = new Vector2(12f, 12f);
        dot = dotObject.GetComponent<Image>();
        dot.raycastTarget = false;

        var textObject = new GameObject("Value", typeof(RectTransform));
        textObject.transform.SetParent(plate.transform, false);
        var textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(30f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);
        label = textObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 22f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.text = "PING N/A";

        group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + SampleInterval;

        if (!InPlay())
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, 0.5f);
            smoothed = -1f;
            return;
        }
        group.alpha = 1f;

        float milliseconds = ReadRoundTrip();
        if (milliseconds < 0f)
        {
            // No session to measure (bot match, or not connected yet).
            smoothed = -1f;
            Show(-1, Unavailable);
            return;
        }

        // Averaged so the readout settles on what the connection is actually
        // doing instead of flickering on single slow packets.
        smoothed = smoothed < 0f ? milliseconds : Mathf.Lerp(smoothed, milliseconds, 0.25f);
        Color tint = smoothed <= GoodMilliseconds ? Good
            : smoothed <= FairMilliseconds ? Fair
            : Poor;
        Show(Mathf.RoundToInt(smoothed), tint);
    }

    private void Show(int value, Color tint)
    {
        if (value != shownValue)
        {
            shownValue = value;
            label.text = value < 0 ? "PING N/A" : "PING " + value + " ms";
        }
        label.color = tint;
        dot.color = tint;
    }

    /// <summary>Lobby, search, match or bot match - not the menus or the result screen.</summary>
    private static bool InPlay()
    {
        var flow = FrontendFlow.Instance;
        if (flow == null) return MatchSessionContext.Type == MatchType.HumanOnline;
        var state = flow.State;
        return state == FrontendState.Matchmaking || state == FrontendState.Gameplay || state == FrontendState.PrivateRoom;
    }

    /// <summary>Local round trip to the Photon server in milliseconds, or -1 when
    /// there is no online session to measure.</summary>
    private static float ReadRoundTrip()
    {
        var director = OnlineMatchDirector.Instance;
        var runner = director != null ? director.Runner : null;
        if (runner == null || !runner.IsRunning)
        {
            // Still searching: the matchmaking runner is the live connection.
            var backend = MatchmakingController.Instance != null ? MatchmakingController.Instance.Backend as FusionMatchmakingBackend : null;
            runner = backend != null ? backend.ActiveRunner : null;
        }
        if (runner == null || !runner.IsRunning)
        {
            var lobby = PrivateLobbyDirector.Instance;
            runner = lobby != null ? lobby.Runner : null;
        }
        if (runner == null || !runner.IsRunning) return -1f;

        // Guarded: Fusion throws if the runner shuts down between the check and
        // the read, and a HUD readout must never take the match down with it.
        try
        {
            // GetRttToPhotonCloud, not GetPlayerRtt. This game runs Shared Mode,
            // where a client has no server peer to measure a player round trip
            // against. Fusion documents the cloud round trip as the one
            // measurement valid in every game mode.
            //
            // It reports zero until the peer is actually connected, so zero means
            // "nothing measured yet" and reads N/A rather than claiming 0 ms.
            double milliseconds = runner.GetRttToPhotonCloud().average * 1000d;
            return milliseconds > 0d ? (float)milliseconds : -1f;
        }
        catch
        {
            return -1f;
        }
    }
}
