using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PLAY's matchmaking screen: a full, opaque Robo Mania screen rather than a
/// translucent panel over whatever was behind it (the menu, or the arena of the
/// match just played). The player's own robot card on the left, a VS badge,
/// and a radar card on the right that sweeps until a match is found.
///
/// States: Searching ("SEARCHING FOR OPPONENT" / "Searching..."), Finding (the
/// bot fallback: "FINDING MATCH..."), Found (a human joined). Nothing technical
/// is shown. Animation touches transforms and colours only; text is rebuilt
/// only when it actually changes.
/// </summary>
public sealed class MatchmakingScreen : MonoBehaviour
{
    public enum Phase { Searching, Finding, Found }

    private static Texture2D ringTexture, sweepTexture;

    private TextMeshProUGUI title, status, opponentName;
    private RectTransform sweep, radar, question;
    private Image opponentFrame;
    private CanvasGroup cancelGroup;
    private Phase phase = Phase.Searching;
    private int shownDots = -1;
    private float phaseStarted;

    private static readonly Color CardFace = new Color(.07f, .16f, .36f);
    private static readonly Color CardInner = new Color(.10f, .24f, .52f);
    private static readonly Color Muted = new Color(.72f, .82f, .96f);

    public void Build(RectTransform root, RectTransform safe, UnityEngine.Events.UnityAction cancel)
    {
        FrontendBackdrop.Create(root);

        title = FrontendUI.Text("Title", safe, "SEARCHING FOR OPPONENT", new Vector2(.12f, .80f), new Vector2(.88f, .93f), 66, FrontendUI.Cream);
        status = FrontendUI.Text("Status", safe, "Searching", new Vector2(.25f, .205f), new Vector2(.75f, .28f), 34, Muted);

        // Player card.
        var you = Card(safe, "YouCard", new Vector2(.205f, .31f), new Vector2(.425f, .755f), FrontendUI.Gold);
        FrontendUI.Text("Heading", you, "YOU", new Vector2(.05f, .86f), new Vector2(.95f, .97f), 32, FrontendUI.Gold);
        // The player's own robot, live and outlined - the same card stage as the lobby.
        FrontendBackdrop.Stage(you, new Vector2(.06f, .22f), new Vector2(.94f, .86f));
        var robot = FrontendUI.Rect("Robot", you, new Vector2(.06f, .22f), new Vector2(.94f, .86f)).gameObject.AddComponent<RawImage>();
        robot.raycastTarget = false;
        var presenter = robot.gameObject.AddComponent<RobotPreviewPresenter>();
        if (presenter.Initialize(null, robot, false)) presenter.EnableSilhouetteOutline(3.5f);
        FrontendUI.Text("Name", you, PlayerProfileService.DisplayName, new Vector2(.05f, .06f), new Vector2(.95f, .20f), 32, FrontendUI.Cream);

        // VS badge.
        var vs = FrontendUI.Panel("VsBadge", safe, new Vector2(.458f, .47f), new Vector2(.542f, .60f), FrontendUI.Ink);
        vs.raycastTarget = false;
        var vsFace = FrontendUI.Inset("Face", vs.transform, FrontendUI.Gold, 5f, 8f, 5f, 5f);
        vsFace.raycastTarget = false;
        FrontendUI.Text("Label", vsFace.transform, "VS", Vector2.zero, Vector2.one, 54, FrontendUI.Ink).outlineWidth = 0f;

        // Opponent card: radar until found.
        var them = Card(safe, "OpponentCard", new Vector2(.575f, .31f), new Vector2(.795f, .755f), FrontendUI.Cyan);
        opponentFrame = them.GetComponent<Image>();
        FrontendUI.Text("Heading", them, "OPPONENT", new Vector2(.05f, .86f), new Vector2(.95f, .97f), 32, FrontendUI.Cyan);
        var scope = FrontendUI.Panel("Scope", them, new Vector2(.08f, .24f), new Vector2(.92f, .85f), CardInner);
        scope.raycastTarget = false;
        radar = FrontendUI.Rect("Radar", scope.transform, new Vector2(.06f, .06f), new Vector2(.94f, .94f));
        var radarFit = radar.gameObject.AddComponent<AspectRatioFitter>();
        radarFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        radarFit.aspectRatio = 1f;
        var rings = FrontendUI.Rect("Rings", radar, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        rings.texture = Rings;
        rings.color = new Color(.55f, .85f, 1f, .55f);
        rings.raycastTarget = false;
        sweep = FrontendUI.Rect("Sweep", radar, Vector2.zero, Vector2.one);
        var sweepImage = sweep.gameObject.AddComponent<RawImage>();
        sweepImage.texture = Sweep;
        sweepImage.color = new Color(.35f, .88f, 1f, .9f);
        sweepImage.raycastTarget = false;
        question = FrontendUI.Rect("Question", radar, new Vector2(.28f, .28f), new Vector2(.72f, .72f));
        var mark = FrontendUI.Text("Mark", question, "?", Vector2.zero, Vector2.one, 110, FrontendUI.Cream);
        mark.raycastTarget = false;
        opponentName = FrontendUI.Text("Name", them, "? ? ?", new Vector2(.05f, .06f), new Vector2(.95f, .20f), 32, Muted);

        var cancelButton = FrontendUI.Button("CancelSearch", safe, "CANCEL", new Vector2(.40f, .055f), new Vector2(.60f, .165f), FrontendUI.Blue, cancel);
        cancelGroup = cancelButton.gameObject.AddComponent<CanvasGroup>();
        phaseStarted = Time.unscaledTime;
        UpdateStatus();
    }

    private static RectTransform Card(RectTransform parent, string name, Vector2 min, Vector2 max, Color frame)
    {
        var outer = FrontendUI.Panel(name, parent, min, max, frame);
        outer.raycastTarget = false;
        var face = FrontendUI.Inset("Face", outer.transform, CardFace, 6f, 6f, 6f, 6f);
        face.raycastTarget = false;
        return (RectTransform)outer.transform;
    }

    public void SetPhase(Phase next)
    {
        if (phase == next) return;
        phase = next;
        phaseStarted = Time.unscaledTime;
        shownDots = -1;
        if (next == Phase.Finding) title.text = "FINDING MATCH";
        if (next == Phase.Found)
        {
            title.text = "OPPONENT FOUND";
            opponentName.text = "READY!";
            opponentName.color = FrontendUI.Gold;
            opponentFrame.color = FrontendUI.Gold;
        }
        // Cancel only while a human search is actually running.
        cancelGroup.alpha = next == Phase.Searching ? 1f : 0f;
        cancelGroup.interactable = cancelGroup.blocksRaycasts = next == Phase.Searching;
        UpdateStatus();
    }

    private void Update()
    {
        float t = Time.unscaledTime;
        if (sweep != null) sweep.localEulerAngles = new Vector3(0f, 0f, -t * (phase == Phase.Found ? 40f : 150f));
        if (question != null)
        {
            float pulse = phase == Phase.Found ? 1f : 1f + .06f * Mathf.Sin(t * 4f);
            question.localScale = Vector3.one * pulse;
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int dots = Mathf.FloorToInt((Time.unscaledTime - phaseStarted) * 2.5f) % 4;
        if (dots == shownDots) return;
        shownDots = dots;
        string trail = dots == 0 ? "" : dots == 1 ? "." : dots == 2 ? ".." : "...";
        status.text = phase == Phase.Found ? "Get ready!" : phase == Phase.Finding ? "FINDING MATCH" + trail : "Searching" + trail;
    }

    // ------------------------------------------------------------------ generated art

    private static Texture2D Rings
    {
        get
        {
            if (ringTexture != null) return ringTexture;
            const int size = 128;
            ringTexture = NewTexture("RadarRings", size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    foreach (float ring in new[] { .32f, .62f, .94f })
                        a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - ring) * size * .5f / 1.3f));
                    // Cross hairs.
                    if (r < .94f) a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy)) * size * .5f / .9f) * .6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            ringTexture.SetPixels32(pixels);
            ringTexture.Apply(false, true);
            return ringTexture;
        }
    }

    private static Texture2D Sweep
    {
        get
        {
            if (sweepTexture != null) return sweepTexture;
            const int size = 128;
            sweepTexture = NewTexture("RadarSweep", size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Leading edge at 12 o'clock, fading anticlockwise over 100 degrees.
                    float angle = Mathf.Repeat(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg, 360f);
                    float trail = angle > 260f ? (angle - 260f) / 100f : 0f;
                    float edge = Mathf.Clamp01(1f - (r - .92f) * size * .5f);
                    float a = trail * trail * .75f * edge * (r < .94f ? 1f : 0f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            sweepTexture.SetPixels32(pixels);
            sweepTexture.Apply(false, true);
            return sweepTexture;
        }
    }

    private static Texture2D NewTexture(string name, int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
    {
        name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
    };
}
