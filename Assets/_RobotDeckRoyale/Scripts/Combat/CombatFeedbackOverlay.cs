using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(20000)]
public class CombatFeedbackOverlay : MonoBehaviour
{
    private static CombatFeedbackOverlay instance;

    private Image[] damageIndicators;
    private Image[] hitMarker;
    private float damageStrength;
    private float markerStrength;
    private float pauseUntilRealtime;
    private float timeScaleBeforePause = 1f;
    private float appliedTimeScale = 1f;
    private bool pauseActive;
    private float lastPauseStartedAt = -999f;

    /// <summary>
    /// Minimum real seconds between impact pauses. Without it a fast weapon
    /// requests a pause on every shot and the game runs permanently in slow
    /// motion instead of punctuating individual hits.
    /// </summary>
    private const float MinimumPauseInterval = 0.14f;
    private float damageAngle;
    private bool hasDamageDirection;
    private TMPro.TextMeshProUGUI respawnBanner;

    public static CombatFeedbackOverlay GetOrCreate()
    {
        if (instance != null)
            return instance;

        GameObject root = new GameObject("Combat Feedback Overlay");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CombatFeedbackOverlay>();
        instance.BuildCanvas();
        return instance;
    }

    public void PulseDamage(float strength)
    {
        PulseDamage(strength, Vector3.zero);
    }

    /// <summary>
    /// <paramref name="worldDirection"/> points from the player toward the
    /// attacker. When supplied, the indicator rotates to that bearing so the
    /// player learns where the threat is instead of only that they were hit.
    /// </summary>
    public void PulseDamage(float strength, Vector3 worldDirection)
    {
        damageStrength = Mathf.Max(damageStrength, Mathf.Clamp01(strength));

        if (worldDirection.sqrMagnitude <= 0.0001f)
        {
            hasDamageDirection = false;
            return;
        }

        Camera camera = Camera.main;

        if (camera == null)
        {
            hasDamageDirection = false;
            return;
        }

        // Project the world bearing into screen space so the arc matches what
        // the angled top-down camera is actually showing.
        Vector3 screenDirection =
            camera.transform.InverseTransformDirection(worldDirection);

        Vector2 flat = new Vector2(screenDirection.x, screenDirection.z);

        if (flat.sqrMagnitude <= 0.0001f)
        {
            hasDamageDirection = false;
            return;
        }

        damageAngle = Mathf.Atan2(flat.y, flat.x) * Mathf.Rad2Deg;
        hasDamageDirection = true;
    }

    /// <summary>
    /// Single screen-space respawn banner for the local robot, anchored near the
    /// top of the screen so it is readable regardless of where the player died.
    /// </summary>
    private RespawnCountdownUI sharedRespawn;
    public void ShowRespawnCountdown(int secondsRemaining)
    {
        if (sharedRespawn == null) sharedRespawn = RespawnCountdownUI.Create(transform);
        sharedRespawn.Show(true, Mathf.Max(1, secondsRemaining));
    }
    public void HideRespawnCountdown()
    {
        if (sharedRespawn != null) sharedRespawn.Show(false, 0);
    }

    public void PulseHitConfirm(float strength)
    {
        markerStrength = Mathf.Max(markerStrength, Mathf.Clamp01(strength));
    }

    public void RequestImpactPause(float duration, float timeScale)
    {
        RequestImpactPause(duration, timeScale, false);
    }

    /// <summary>
    /// <paramref name="ignoreRateLimit"/> is for one-off beats such as a kill,
    /// which should always land even if a hit just paused the game.
    /// </summary>
    public void RequestImpactPause(float duration, float timeScale, bool ignoreRateLimit)
    {
        // A match uses a consistent real-time cadence in both simulation paths.
        // Global hit-stop slowed the bot path while Fusion continued ticking.
        if (MatchSessionContext.Type != MatchType.None || duration <= 0f)
            return;

        // Never engage while the game is already stopped by a menu or a match
        // transition, otherwise expiry would restore a timescale of zero.
        if (!pauseActive && Time.timeScale <= 0.01f)
            return;

        if (!ignoreRateLimit &&
            Time.realtimeSinceStartup - lastPauseStartedAt < MinimumPauseInterval)
        {
            return;
        }

        lastPauseStartedAt = Time.realtimeSinceStartup;

        if (!pauseActive)
        {
            timeScaleBeforePause = Time.timeScale;
            pauseActive = true;
        }

        pauseUntilRealtime = Mathf.Max(pauseUntilRealtime, Time.realtimeSinceStartup + duration);
        Time.timeScale = Mathf.Min(Time.timeScale, Mathf.Clamp(timeScale, 0.05f, 1f));
        appliedTimeScale = Time.timeScale;
    }

    /// <summary>
    /// Only restores the timescale this component actually set. If anything else
    /// changed it during the hit pause, that newer value is authoritative and
    /// must not be overwritten by a stale captured value.
    /// </summary>
    private void ReleaseImpactPause()
    {
        if (!pauseActive)
            return;

        pauseActive = false;

        if (Mathf.Approximately(Time.timeScale, appliedTimeScale))
            Time.timeScale = timeScaleBeforePause;
    }

    private void Update()
    {
        damageStrength = Mathf.MoveTowards(damageStrength, 0f, Time.unscaledDeltaTime * 3.8f);
        markerStrength = Mathf.MoveTowards(markerStrength, 0f, Time.unscaledDeltaTime * 6.5f);
        ApplyVisuals();

        if (pauseActive && Time.realtimeSinceStartup >= pauseUntilRealtime)
            ReleaseImpactPause();
    }

    private void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
    private void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;

    // This overlay outlives scenes; a respawn countdown never legitimately does.
    // A match left mid-respawn (lost connection, Play Again) would otherwise keep
    // "RESPAWNING IN n" frozen over the next screen.
    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single) HideRespawnCountdown();
    }

    private void OnDestroy()
    {
        ReleaseImpactPause();

        if (instance == this)
            instance = null;
    }

    private void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        gameObject.AddComponent<GraphicRaycaster>().enabled = false;

        // Taking damage is communicated close to the aiming point, never by a
        // full-screen wash or red vignette. The robot flash, explosion and
        // camera impulse remain readable without colouring the arena.
        damageIndicators = new Image[4];
        for (int index = 0; index < damageIndicators.Length; index++)
        {
            Image line = CreateImage(
                "Damage Indicator " + index,
                transform,
                new Color(1f, 0.72f, 0.16f, 0f));
            RectTransform rect = line.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(24f, 4f);

            float angle = index * 90f;
            Vector2 direction = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad));
            rect.anchoredPosition = direction * 54f;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle + 90f);
            damageIndicators[index] = line;
        }

        hitMarker = new Image[4];
        for (int index = 0; index < hitMarker.Length; index++)
        {
            Image line = CreateImage(
                "Hit Marker " + index,
                transform,
                new Color(1f, 0.78f, 0.18f, 0f));
            RectTransform rect = line.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(19f, 3.5f);
            float side = index < 2 ? -1f : 1f;
            float vertical = index % 2 == 0 ? -1f : 1f;
            rect.anchoredPosition = new Vector2(side * 14f, vertical * 14f);
            rect.localRotation = Quaternion.Euler(0f, 0f, side * vertical * 45f);
            hitMarker[index] = line;
        }
    }

    private void ApplyVisuals()
    {
        if (damageIndicators == null)
            return;

        float damageScale = Mathf.Lerp(1.35f, 0.88f, 1f - damageStrength);

        for (int index = 0; index < damageIndicators.Length; index++)
        {
            Image indicator = damageIndicators[index];

            if (indicator == null)
                continue;

            float baseAngle = index * 90f;

            // With a known bearing only the tick facing the attacker is lit, so
            // the indicator reads as a direction rather than a generic flash.
            float visibility = 1f;

            if (hasDamageDirection)
            {
                float delta = Mathf.Abs(
                    Mathf.DeltaAngle(baseAngle, damageAngle));
                visibility = Mathf.Clamp01(1f - delta / 90f);
                baseAngle = Mathf.LerpAngle(baseAngle, damageAngle, visibility);
            }

            SetAlpha(indicator, damageStrength * 0.8f * visibility);

            RectTransform rect = indicator.rectTransform;
            rect.localScale = Vector3.one * damageScale;
            rect.anchoredPosition = new Vector2(
                Mathf.Cos(baseAngle * Mathf.Deg2Rad),
                Mathf.Sin(baseAngle * Mathf.Deg2Rad)) * 54f;
            rect.localRotation = Quaternion.Euler(0f, 0f, baseAngle + 90f);
        }

        float markerScale = Mathf.Lerp(1.3f, 0.82f, 1f - markerStrength);
        foreach (Image line in hitMarker)
        {
            SetAlpha(line, markerStrength * 0.95f);
            line.rectTransform.localScale = Vector3.one * markerScale;
        }
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(parent, false);
        Image image = child.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetAlpha(Image image, float alpha)
    {
        if (image == null)
            return;
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }
}
