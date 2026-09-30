using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// Brawl Stars-style scrap counter with chunky visuals and juice feedback.
/// Detects value changes and plays punch/shake/count animations.
/// Builds its own background panel programmatically to match the code-built UI pattern.
/// </summary>
public class ScrapCounterUI : MonoBehaviour
{
    [SerializeField] private ScrapCollector collector;
    [SerializeField] private TMP_Text scrapText;

    private int lastScrap = -1;
    private Coroutine countCoroutine;
    private int displayedValue;

    // Star burst particle pool
    private readonly RectTransform[] starPool = new RectTransform[8];
    private readonly float[] starLife = new float[8];
    private readonly Vector2[] starVelocity = new Vector2[8];
    private Canvas parentCanvas;

    private void Start()
    {
        if (scrapText == null)
        {
            return;
        }

        // Style the text for that chunky Brawl Stars look.
        scrapText.fontStyle = FontStyles.Bold;
        scrapText.outlineColor = new Color(0.06f, 0.02f, 0.12f, 1f);
        scrapText.outlineWidth = 0.25f;

        // Build a background panel behind the counter if one doesn't already exist.
        BuildBackgroundPanel();

        // Pre-create star burst particle pool as sibling UI elements.
        parentCanvas = scrapText.GetComponentInParent<Canvas>();
        Transform starParent = scrapText.transform.parent != null ? scrapText.transform.parent : scrapText.transform;
        for (int i = 0; i < starPool.Length; i++)
        {
            GameObject star = new GameObject("ScrapStar_" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            star.transform.SetParent(starParent, false);
            RectTransform rt = star.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(8f, 8f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f); // Diamond shape
            Image img = star.GetComponent<Image>();
            img.color = new Color(1f, 0.84f, 0.18f, 0f); // Yellow, invisible by default
            img.raycastTarget = false;
            starPool[i] = rt;
            starLife[i] = 0f;
        }

        // Initialize display.
        if (collector != null)
        {
            lastScrap = collector.Scrap;
            displayedValue = lastScrap;
            scrapText.text = FormatScrap(displayedValue);
        }
    }

    private void Update()
    {
        if (collector == null || scrapText == null)
        {
            return;
        }

        int current = collector.Scrap;

        // Detect value change.
        if (current != lastScrap)
        {
            int previous = lastScrap;
            lastScrap = current;

            if (current > previous)
            {
                // Gained scrap — punchy positive feedback.
                OnScrapGained(previous, current);
            }
            else
            {
                // Spent scrap — subtle negative feedback.
                OnScrapSpent(previous, current);
            }
        }

        // Animate star burst particles.
        UpdateStarParticles();
    }

    private void OnScrapGained(int from, int to)
    {
        // Animated count-up.
        if (countCoroutine != null)
        {
            StopCoroutine(countCoroutine);
        }
        countCoroutine = StartCoroutine(AnimateCount(displayedValue, to, 0.35f));

        // Punch scale on the text — satisfying pop.
        UITweenEngine.PunchScale(scrapText.transform, 1.25f, 0.18f);

        // Spawn star burst particles.
        SpawnStarBurst();

        // Play collect sound.
        UIAudioManager.Play(UISoundType.ScrapCollect);
    }

    private void OnScrapSpent(int from, int to)
    {
        // Animated count-down (faster).
        if (countCoroutine != null)
        {
            StopCoroutine(countCoroutine);
        }
        countCoroutine = StartCoroutine(AnimateCount(displayedValue, to, 0.2f));

        // Shake for error/spend feedback.
        UITweenEngine.ShakeTransform(scrapText.transform, 3f, 0.15f);
    }

    private IEnumerator AnimateCount(int from, int to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Ease-out curve for snappy feel.
            t = 1f - (1f - t) * (1f - t);
            displayedValue = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
            scrapText.text = FormatScrap(displayedValue);
            yield return null;
        }

        displayedValue = to;
        scrapText.text = FormatScrap(to);
        countCoroutine = null;
    }

    private void SpawnStarBurst()
    {
        Vector2 origin = scrapText.rectTransform.anchoredPosition;
        for (int i = 0; i < starPool.Length; i++)
        {
            if (starLife[i] > 0f)
            {
                continue; // Skip stars that are still alive.
            }

            starLife[i] = 0.4f;
            float angle = (i / (float)starPool.Length) * 360f + Random.Range(-20f, 20f);
            float speed = Random.Range(120f, 220f);
            starVelocity[i] = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad) * speed,
                Mathf.Sin(angle * Mathf.Deg2Rad) * speed
            );
            starPool[i].anchoredPosition = origin;
            starPool[i].sizeDelta = new Vector2(Random.Range(6f, 10f), Random.Range(6f, 10f));
        }
    }

    private void UpdateStarParticles()
    {
        for (int i = 0; i < starPool.Length; i++)
        {
            if (starLife[i] <= 0f)
            {
                continue;
            }

            starLife[i] -= Time.deltaTime;
            float alpha = Mathf.Clamp01(starLife[i] / 0.4f);

            starPool[i].anchoredPosition += starVelocity[i] * Time.deltaTime;
            // Slow down over time.
            starVelocity[i] *= 1f - 3f * Time.deltaTime;

            Image img = starPool[i].GetComponent<Image>();
            if (img != null)
            {
                // Cycle between yellow and cyan stars.
                Color starColor = i % 2 == 0
                    ? new Color(1f, 0.84f, 0.18f, alpha)
                    : new Color(0.16f, 0.90f, 1f, alpha);
                img.color = starColor;
            }

            if (starLife[i] <= 0f)
            {
                Image deadImg = starPool[i].GetComponent<Image>();
                if (deadImg != null)
                {
                    deadImg.color = new Color(1f, 1f, 1f, 0f);
                }
            }
        }
    }

    private void BuildBackgroundPanel()
    {
        Transform textParent = scrapText.transform.parent;
        if (textParent == null)
        {
            return;
        }

        // Check if a background already exists (don't double-build).
        if (textParent.Find("ScrapCounterBG") != null)
        {
            return;
        }

        // Create a chunky background panel behind the text.
        GameObject bg = new GameObject("ScrapCounterBG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(textParent, false);
        bg.transform.SetSiblingIndex(scrapText.transform.GetSiblingIndex());

        RectTransform bgRect = bg.GetComponent<RectTransform>();
        // Copy the text's rect and expand it slightly for padding.
        RectTransform textRect = scrapText.rectTransform;
        bgRect.anchorMin = textRect.anchorMin;
        bgRect.anchorMax = textRect.anchorMax;
        bgRect.pivot = textRect.pivot;
        bgRect.anchoredPosition = textRect.anchoredPosition;
        bgRect.sizeDelta = textRect.sizeDelta + new Vector2(30f, 16f);

        Image bgImage = bg.GetComponent<Image>();
        bgImage.color = new Color(0.06f, 0.03f, 0.14f, 0.85f);
        bgImage.raycastTarget = false;

        // Add thick border via Outline for Brawl Stars panel feel.
        UnityEngine.UI.Outline bgOutline = bg.AddComponent<UnityEngine.UI.Outline>();
        bgOutline.effectColor = new Color(0.03f, 0.01f, 0.08f, 1f);
        bgOutline.effectDistance = new Vector2(2f, -2f);

        // Drop shadow offset for 3D depth.
        GameObject shadow = new GameObject("ScrapCounterShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shadow.transform.SetParent(textParent, false);
        shadow.transform.SetSiblingIndex(bg.transform.GetSiblingIndex());

        RectTransform shadowRect = shadow.GetComponent<RectTransform>();
        shadowRect.anchorMin = bgRect.anchorMin;
        shadowRect.anchorMax = bgRect.anchorMax;
        shadowRect.pivot = bgRect.pivot;
        shadowRect.anchoredPosition = bgRect.anchoredPosition + new Vector2(3f, -3f);
        shadowRect.sizeDelta = bgRect.sizeDelta;

        Image shadowImage = shadow.GetComponent<Image>();
        shadowImage.color = new Color(0.02f, 0.01f, 0.06f, 0.5f);
        shadowImage.raycastTarget = false;
    }

    private static string FormatScrap(int value)
    {
        return "\u2699 " + value.ToString("N0");
    }
}
