using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Arena-first match presentation. The live arena remains visible while compact,
/// opposing team banners and a central callout carry the match-state information.
/// </summary>
public sealed class MatchPresentationUI : MonoBehaviour
{
    private static Sprite solidSprite;

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform panel;
    private Image panelImage;
    private RectTransform blueBannerBack;
    private RectTransform redBannerBack;
    private RectTransform blueBanner;
    private RectTransform redBanner;
    private RectTransform centreBacking;
    private RectTransform centreBackingAccent;
    private RectTransform eyebrowBacking;
    private RectTransform detailBacking;
    private RectTransform rewardBacking;
    private TMP_Text eyebrow;
    private TMP_Text blueName;
    private TMP_Text redName;
    private TMP_Text centreShadow;
    private TMP_Text centreLabel;
    private TMP_Text detail;
    private TMP_Text rewardLabel;
    private Image blueAccent;
    private Image redAccent;
    private bool isBuilt;
    private Coroutine animationRoutine;

    public void EnsureBuilt()
    {
        if (isBuilt)
            return;

        canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 140;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        group = GetComponent<CanvasGroup>();
        if (group == null)
            group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        // This root deliberately has no dark card. The arena is the background.
        panel = CreateRect("Match Presentation", transform);
        SetRect(panel, Vector2.zero, Vector2.one);
        panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = GetSolidSprite();
        panelImage.color = Color.clear;
        panelImage.raycastTarget = false;

        // The offset black layers add depth without consuming more screen space.
        blueBannerBack = CreateStripe(panel, "Blue Banner Shadow",
            new Color(0.004f, 0.01f, 0.025f, 0.92f),
            new Vector2(0.012f, 0.105f), new Vector2(0.505f, 0.305f), 2.5f).rectTransform;
        redBannerBack = CreateStripe(panel, "Red Banner Shadow",
            new Color(0.004f, 0.01f, 0.025f, 0.92f),
            new Vector2(0.495f, 0.695f), new Vector2(0.988f, 0.895f), -2.5f).rectTransform;

        Image blueBannerImage = CreateStripe(panel, "Blue Team Banner",
            new Color(0.025f, 0.30f, 0.78f, 0.96f),
            new Vector2(0.018f, 0.12f), new Vector2(0.492f, 0.29f), 2.5f);
        blueBanner = blueBannerImage.rectTransform;
        Image redBannerImage = CreateStripe(panel, "Red Team Banner",
            new Color(0.80f, 0.055f, 0.07f, 0.96f),
            new Vector2(0.508f, 0.71f), new Vector2(0.982f, 0.88f), -2.5f);
        redBanner = redBannerImage.rectTransform;

        blueAccent = CreateStripe(blueBanner, "Blue Highlight",
            new Color(0.16f, 0.76f, 1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0.055f));
        redAccent = CreateStripe(redBanner, "Red Highlight",
            new Color(1f, 0.36f, 0.18f, 1f),
            new Vector2(0f, 0.945f), new Vector2(1f, 1f));

        blueName = CreateText(blueBanner, "Blue Name", new Vector2(0.07f, 0.08f),
            new Vector2(0.93f, 0.94f), 58f, TextAlignmentOptions.Center, Color.white);
        blueName.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
        redName = CreateText(redBanner, "Red Name", new Vector2(0.07f, 0.06f),
            new Vector2(0.93f, 0.92f), 58f, TextAlignmentOptions.Center, Color.white);
        redName.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);

        centreBackingAccent = CreateStripe(panel, "Centre Accent",
            new Color(1f, 0.68f, 0.08f, 0.98f),
            new Vector2(0.437f, 0.387f), new Vector2(0.563f, 0.613f), 45f).rectTransform;
        centreBacking = CreateStripe(panel, "Centre Backing",
            new Color(0.005f, 0.008f, 0.018f, 0.94f),
            new Vector2(0.444f, 0.398f), new Vector2(0.556f, 0.602f), 45f).rectTransform;

        centreShadow = CreateText(panel, "Centre Shadow", new Vector2(0.34f, 0.33f),
            new Vector2(0.67f, 0.67f), 128f, TextAlignmentOptions.Center,
            new Color(0f, 0f, 0f, 0.92f));
        centreShadow.rectTransform.anchoredPosition = new Vector2(8f, -10f);
        centreShadow.outlineWidth = 0.36f;
        centreLabel = CreateText(panel, "Centre Label", new Vector2(0.34f, 0.33f),
            new Vector2(0.67f, 0.67f), 128f, TextAlignmentOptions.Center, Color.white);
        centreLabel.outlineWidth = 0.34f;

        eyebrowBacking = CreateStripe(panel, "Eyebrow Backing",
            new Color(0.005f, 0.01f, 0.025f, 0.82f),
            new Vector2(0.38f, 0.70f), new Vector2(0.62f, 0.765f)).rectTransform;
        eyebrow = CreateText(panel, "Eyebrow", new Vector2(0.36f, 0.69f),
            new Vector2(0.64f, 0.775f), 27f, TextAlignmentOptions.Center,
            new Color(0.88f, 0.93f, 1f, 1f));

        detailBacking = CreateStripe(panel, "Detail Backing",
            new Color(0.005f, 0.01f, 0.025f, 0.82f),
            new Vector2(0.30f, 0.235f), new Vector2(0.70f, 0.305f)).rectTransform;
        detail = CreateText(panel, "Detail", new Vector2(0.28f, 0.225f),
            new Vector2(0.72f, 0.315f), 24f, TextAlignmentOptions.Center,
            new Color(0.88f, 0.92f, 0.98f, 1f));

        rewardBacking = CreateStripe(panel, "Reward Backing",
            new Color(0.09f, 0.055f, 0.005f, 0.94f),
            new Vector2(0.405f, 0.285f), new Vector2(0.595f, 0.35f)).rectTransform;
        rewardLabel = CreateText(panel, "Coin Reward", new Vector2(0.39f, 0.275f),
            new Vector2(0.61f, 0.36f), 32f, TextAlignmentOptions.Center,
            new Color(1f, 0.76f, 0.08f, 1f));
        rewardLabel.outlineWidth = 0.34f;
        SetRewardVisible(false);

        isBuilt = true;
        panel.gameObject.SetActive(false);
    }

    public void ShowVersus(string blue, string red, float duration)
    {
        EnsureBuilt();
        ConfigureVersusLayout();
        SetNames(blue, red);
        centreLabel.text = "VS";
        centreShadow.text = centreLabel.text;
        centreLabel.fontSize = 132f;
        centreShadow.fontSize = centreLabel.fontSize;
        AnimateTransient(duration, 0.74f, 1.08f);
    }

    public void ShowBuildPhase(float duration)
    {
        EnsureBuilt();
        ConfigurePhaseLayout(new Color(0.08f, 0.52f, 1f, 1f));
        eyebrow.text = "FORTRESS DEPLOYMENT";
        centreLabel.text = "BUILD";
        centreShadow.text = centreLabel.text;
        detail.text = "PLACE DEFENCES  //  SYSTEMS ONLINE";
        centreLabel.fontSize = 116f;
        centreShadow.fontSize = centreLabel.fontSize;
        AnimateTransient(Mathf.Min(1.05f, duration), 0.78f, 1f);
    }

    /// <summary>Closes the build phase. Same treatment as Rumble, different words.</summary>
    public void ShowFight(float duration)
    {
        EnsureBuilt();
        ConfigurePhaseLayout(new Color(1f, 0.32f, 0.12f, 1f));
        eyebrow.text = "DEFENCES ONLINE";
        centreLabel.text = "FIGHT!";
        centreShadow.text = centreLabel.text;
        detail.text = "BREAK THE ENEMY HEIST";
        centreLabel.fontSize = 148f;
        centreShadow.fontSize = centreLabel.fontSize;
        AnimateTransient(duration, 0.68f, 1.1f);
    }

    public void ShowRumble(float duration)
    {
        EnsureBuilt();
        ConfigurePhaseLayout(new Color(1f, 0.58f, 0.06f, 1f));
        eyebrow.text = "BUILD COMPLETE";
        centreLabel.text = "RUMBLE";
        centreShadow.text = centreLabel.text;
        detail.text = "BREAK THE ENEMY CORE";
        centreLabel.fontSize = 148f;
        centreShadow.fontSize = centreLabel.fontSize;
        AnimateTransient(duration, 0.68f, 1.1f);
    }

    public void ShowResult(
        FortressTeam? winner,
        string blue,
        string red,
        int blueHealth,
        int blueMaxHealth,
        int redHealth,
        int redMaxHealth,
        string reason,
        int awardedCoins = 0)
    {
        if (FrontendFlow.Instance != null)
        {
            HideTransientPresentation();
            FrontendFlow.Instance.ShowResult(winner, awardedCoins);
            return;
        }
        EnsureBuilt();
        StopAnimation();
        ConfigureResultLayout(winner);
        SetNames(blue, red);
        eyebrow.text = winner.HasValue ? "MATCH COMPLETE" : "TIME LIMIT";
        centreLabel.text = !winner.HasValue
            ? "DRAW"
            : winner.Value == FortressTeam.Blue ? "VICTORY" : "DEFEAT";
        centreShadow.text = centreLabel.text;
        centreLabel.fontSize = 112f;
        centreShadow.fontSize = centreLabel.fontSize;
        rewardLabel.text = "+" + Mathf.Max(0, awardedCoins) + " ARENA COINS";
        detail.text = "BLUE HEIST " + Mathf.RoundToInt(Percent(blueHealth, blueMaxHealth) * 100f) +
            "%   //   RED HEIST " + Mathf.RoundToInt(Percent(redHealth, redMaxHealth) * 100f) +
            "%   //   " + reason;
        panel.gameObject.SetActive(true);
        group.alpha = 0f;
        panel.localScale = Vector3.one;
        SetBannerSlide(1f);
        SetCentreScale(0.76f);
        blueAccent.color = new Color(0.16f, 0.76f, 1f, 1f);
        redAccent.color = new Color(1f, 0.36f, 0.18f, 1f);
        animationRoutine = StartCoroutine(AnimateResultEntrance());
    }

    public void HideTransientPresentation()
    {
        EnsureBuilt();
        StopAnimation();
        group.alpha = 0f;
        panel.gameObject.SetActive(false);
    }

    private void ConfigureVersusLayout()
    {
        SetTeamBannersVisible(true);
        SetInfoVisible(false);
        SetRewardVisible(false);
        panelImage.color = Color.clear;
        centreBacking.gameObject.SetActive(true);
        centreBackingAccent.gameObject.SetActive(true);
        SetRect(centreBackingAccent, new Vector2(0.437f, 0.387f), new Vector2(0.563f, 0.613f));
        SetRect(centreBacking, new Vector2(0.444f, 0.398f), new Vector2(0.556f, 0.602f));
        centreBackingAccent.localRotation = Quaternion.Euler(0f, 0f, 45f);
        centreBacking.localRotation = Quaternion.Euler(0f, 0f, 45f);
        centreBackingAccent.GetComponent<Image>().color = new Color(1f, 0.68f, 0.08f, 0.98f);
        centreBacking.GetComponent<Image>().color = new Color(0.005f, 0.008f, 0.018f, 0.94f);
        SetCentreTextRect(new Vector2(0.34f, 0.33f), new Vector2(0.67f, 0.67f));
    }

    private void ConfigurePhaseLayout(Color accentColour)
    {
        SetTeamBannersVisible(false);
        SetInfoVisible(true);
        SetRewardVisible(false);
        SetRect(eyebrowBacking, new Vector2(0.38f, 0.70f), new Vector2(0.62f, 0.765f));
        SetRect(eyebrow.rectTransform, new Vector2(0.36f, 0.69f), new Vector2(0.64f, 0.775f));
        SetRect(detailBacking, new Vector2(0.30f, 0.235f), new Vector2(0.70f, 0.305f));
        SetRect(detail.rectTransform, new Vector2(0.28f, 0.225f), new Vector2(0.72f, 0.315f));
        panelImage.color = Color.clear;
        centreBacking.gameObject.SetActive(true);
        centreBackingAccent.gameObject.SetActive(true);
        SetRect(centreBackingAccent, new Vector2(0.31f, 0.36f), new Vector2(0.69f, 0.64f));
        SetRect(centreBacking, new Vector2(0.315f, 0.37f), new Vector2(0.685f, 0.63f));
        centreBackingAccent.localRotation = Quaternion.Euler(0f, 0f, -1.25f);
        centreBacking.localRotation = Quaternion.Euler(0f, 0f, -1.25f);
        centreBackingAccent.GetComponent<Image>().color = accentColour;
        centreBacking.GetComponent<Image>().color = new Color(0.005f, 0.008f, 0.018f, 0.88f);
        SetCentreTextRect(new Vector2(0.25f, 0.34f), new Vector2(0.75f, 0.66f));
    }

    private void ConfigureResultLayout(FortressTeam? winner)
    {
        SetTeamBannersVisible(true);
        SetInfoVisible(true);
        SetRewardVisible(true);
        // Keep the result copy between the compact team banners so no labels
        // collide and the battlefield remains readable behind all four corners.
        SetRect(eyebrowBacking, new Vector2(0.40f, 0.655f), new Vector2(0.60f, 0.715f));
        SetRect(eyebrow.rectTransform, new Vector2(0.38f, 0.645f), new Vector2(0.62f, 0.725f));
        SetRect(rewardBacking, new Vector2(0.405f, 0.275f), new Vector2(0.595f, 0.34f));
        SetRect(rewardLabel.rectTransform, new Vector2(0.39f, 0.265f), new Vector2(0.61f, 0.35f));
        SetRect(detailBacking, new Vector2(0.31f, 0.205f), new Vector2(0.69f, 0.26f));
        SetRect(detail.rectTransform, new Vector2(0.29f, 0.195f), new Vector2(0.71f, 0.27f));
        panelImage.color = Color.clear;
        centreBacking.gameObject.SetActive(true);
        centreBackingAccent.gameObject.SetActive(true);
        SetRect(centreBackingAccent, new Vector2(0.265f, 0.35f), new Vector2(0.735f, 0.65f));
        SetRect(centreBacking, new Vector2(0.27f, 0.36f), new Vector2(0.73f, 0.64f));
        centreBackingAccent.localRotation = Quaternion.Euler(0f, 0f, -1.25f);
        centreBacking.localRotation = Quaternion.Euler(0f, 0f, -1.25f);

        Color resultColour = !winner.HasValue
            ? new Color(0.95f, 0.72f, 0.12f, 1f)
            : winner.Value == FortressTeam.Blue
                ? new Color(0.08f, 0.65f, 1f, 1f)
                : new Color(1f, 0.20f, 0.12f, 1f);
        centreBackingAccent.GetComponent<Image>().color = resultColour;
        centreBacking.GetComponent<Image>().color = new Color(0.005f, 0.008f, 0.018f, 0.9f);
        SetCentreTextRect(new Vector2(0.22f, 0.33f), new Vector2(0.78f, 0.67f));
    }

    private void SetTeamBannersVisible(bool visible)
    {
        blueBannerBack.gameObject.SetActive(visible);
        redBannerBack.gameObject.SetActive(visible);
        blueBanner.gameObject.SetActive(visible);
        redBanner.gameObject.SetActive(visible);
    }

    private void SetInfoVisible(bool visible)
    {
        eyebrowBacking.gameObject.SetActive(visible);
        eyebrow.gameObject.SetActive(visible);
        detailBacking.gameObject.SetActive(visible);
        detail.gameObject.SetActive(visible);
    }

    private void SetRewardVisible(bool visible)
    {
        rewardBacking.gameObject.SetActive(visible);
        rewardLabel.gameObject.SetActive(visible);
    }

    private void SetCentreTextRect(Vector2 min, Vector2 max)
    {
        SetRect(centreLabel.rectTransform, min, max);
        SetRect(centreShadow.rectTransform, min, max);
        centreShadow.rectTransform.anchoredPosition = new Vector2(8f, -10f);
    }

    private void AnimateTransient(float holdDuration, float startScale, float peakScale)
    {
        StopAnimation();
        panel.gameObject.SetActive(true);
        group.alpha = 0f;
        panel.localScale = Vector3.one;
        SetBannerSlide(1f);
        SetCentreScale(startScale);
        animationRoutine = StartCoroutine(AnimateRoutine(
            Mathf.Max(0.1f, holdDuration),
            Mathf.Max(0.01f, startScale),
            peakScale));
    }

    private IEnumerator AnimateRoutine(float totalDuration, float startScale, float peakScale)
    {
        float entrance = Mathf.Min(0.28f, totalDuration * 0.34f);
        float exit = Mathf.Min(0.22f, totalDuration * 0.28f);
        float hold = Mathf.Max(0f, totalDuration - entrance - exit);
        float elapsed = 0f;

        while (elapsed < entrance)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / entrance);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float pop = t < 0.78f
                ? Mathf.Lerp(startScale, peakScale, eased)
                : Mathf.Lerp(peakScale, 1f, Mathf.InverseLerp(0.78f, 1f, t));
            group.alpha = eased;
            SetBannerSlide(1f - eased);
            SetCentreScale(pop);
            yield return null;
        }

        group.alpha = 1f;
        SetBannerSlide(0f);
        SetCentreScale(1f);
        if (hold > 0f)
            yield return new WaitForSecondsRealtime(hold);

        elapsed = 0f;
        while (elapsed < exit)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / exit);
            group.alpha = 1f - t;
            SetBannerSlide(t * 0.28f);
            SetCentreScale(Mathf.Lerp(1f, 0.92f, t));
            yield return null;
        }

        group.alpha = 0f;
        panel.gameObject.SetActive(false);
        SetBannerSlide(0f);
        SetCentreScale(1f);
        animationRoutine = null;
    }

    private IEnumerator AnimateResultEntrance()
    {
        const float entranceDuration = 0.38f;
        float elapsed = 0f;

        while (elapsed < entranceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / entranceDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float pop = t < 0.76f
                ? Mathf.Lerp(0.76f, 1.055f, eased)
                : Mathf.Lerp(1.055f, 1f, Mathf.InverseLerp(0.76f, 1f, t));

            group.alpha = eased;
            SetBannerSlide(1f - eased);
            SetCentreScale(pop);
            yield return null;
        }

        group.alpha = 1f;
        SetBannerSlide(0f);
        SetCentreScale(1f);
        animationRoutine = null;
    }

    private void SetBannerSlide(float amount)
    {
        float offset = 620f * Mathf.Clamp01(amount);
        blueBannerBack.anchoredPosition = new Vector2(-offset, 0f);
        blueBanner.anchoredPosition = new Vector2(-offset, 0f);
        redBannerBack.anchoredPosition = new Vector2(offset, 0f);
        redBanner.anchoredPosition = new Vector2(offset, 0f);
    }

    private void SetCentreScale(float scale)
    {
        Vector3 value = Vector3.one * scale;
        centreBacking.localScale = value;
        centreBackingAccent.localScale = value;
        centreLabel.rectTransform.localScale = value;
        centreShadow.rectTransform.localScale = value;
    }

    private void StopAnimation()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
    }

    private void SetNames(string blue, string red)
    {
        blueName.text = (blue ?? "BLUE").ToUpperInvariant();
        redName.text = (red ?? "RED").ToUpperInvariant();
    }

    private static float Percent(int value, int max)
    {
        return max > 0 ? Mathf.Clamp01((float)value / max) : 0f;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    private static Image CreateStripe(
        Transform parent,
        string objectName,
        Color colour,
        Vector2 min,
        Vector2 max,
        float angle = 0f)
    {
        RectTransform rect = CreateRect(objectName, parent);
        SetRect(rect, min, max);
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = GetSolidSprite();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string objectName,
        Vector2 min,
        Vector2 max,
        float size,
        TextAlignmentOptions alignment,
        Color colour)
    {
        RectTransform rect = CreateRect(objectName, parent);
        SetRect(rect, min, max);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = alignment;
        text.color = colour;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.outlineColor = new Color(0f, 0f, 0f, 0.98f);
        text.outlineWidth = 0.26f;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite GetSolidSprite()
    {
        if (solidSprite != null)
            return solidSprite;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "Match UI Solid Pixel",
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        solidSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f),
            new Vector2(0.5f, 0.5f), 1f);
        solidSprite.name = "Match UI Solid Sprite";
        solidSprite.hideFlags = HideFlags.DontSave;
        return solidSprite;
    }
}
