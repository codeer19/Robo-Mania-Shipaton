using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small "broken plus" badge above a robot while NO HEAL (Recovery Jammer) is on it.
/// World-space UI like the health bars, so it uses the always-included UI shader
/// and batches with them. Offline the Damageable raises it; online the combat
/// presentation drives it from the replicated timer.
/// </summary>
public sealed class NoHealIndicator : MonoBehaviour
{
    private const string ChildName = "No Heal Indicator";
    private static Sprite badge;
    private float hideAt;
    private bool replicated;
    private RectTransform rect;
    private Image image;
    private Camera cachedCamera;
    // Sits beside the robot's own health bar: the bar already floats clear of
    // every robot model and skin, so the badge can never end up inside the mesh.
    private RectTransform anchor;
    private float sideOffset = 1.2f;
    private const float WorldSize = 1.2f;

    /// <summary>Offline: show for a duration (refreshed by repeated jams).</summary>
    public static void Show(Transform robot, float seconds)
    {
        var indicator = Ensure(robot);
        indicator.replicated = false;
        indicator.hideAt = Mathf.Max(indicator.hideAt, Time.time + seconds);
        indicator.gameObject.SetActive(true);
    }

    /// <summary>Online: follows the authority's timer each frame.</summary>
    public static void SetReplicated(Transform robot, bool active)
    {
        if (robot == null) return;
        var existing = robot.Find(ChildName);
        if (!active && existing == null) return;
        var indicator = Ensure(robot);
        indicator.replicated = true;
        if (indicator.gameObject.activeSelf != active) indicator.gameObject.SetActive(active);
    }

    private static NoHealIndicator Ensure(Transform robot)
    {
        var existing = robot.Find(ChildName);
        if (existing != null) return existing.GetComponent<NoHealIndicator>();
        var go = new GameObject(ChildName, typeof(RectTransform), typeof(Canvas), typeof(NoHealIndicator));
        go.transform.SetParent(robot, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 6;
        var indicator = go.GetComponent<NoHealIndicator>();
        indicator.rect = (RectTransform)go.transform;
        indicator.rect.sizeDelta = new Vector2(100f, 100f);
        float parentScale = Mathf.Max(0.0001f, robot.lossyScale.x);
        indicator.rect.localScale = Vector3.one * (WorldSize / 100f / parentScale);
        var bar = robot.GetComponentInChildren<WorldHealthBar>(true);
        indicator.anchor = bar != null ? bar.transform as RectTransform : null;
        if (indicator.anchor != null)
            indicator.sideOffset = indicator.anchor.rect.width * indicator.anchor.lossyScale.x * 0.5f + WorldSize * 0.6f;
        else
            indicator.rect.localPosition = new Vector3(0f, 3.4f / parentScale, 0f);
        var icon = new GameObject("Badge", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(go.transform, false);
        var iconRect = (RectTransform)icon.transform;
        iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
        indicator.image = icon.GetComponent<Image>();
        indicator.image.sprite = Badge();
        indicator.image.raycastTarget = false;
        return indicator;
    }

    private void LateUpdate()
    {
        if (!replicated && Time.time >= hideAt) { gameObject.SetActive(false); return; }
        if (cachedCamera == null) cachedCamera = Camera.main;
        if (cachedCamera != null)
        {
            rect.rotation = cachedCamera.transform.rotation;
            if (anchor != null) rect.position = anchor.position - cachedCamera.transform.right * sideOffset;
        }
        float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 9f);
        image.rectTransform.localScale = Vector3.one * pulse;
    }

    /// <summary>Dark disc, red rim, cream plus with a red slash: reads at 0.5 m on a phone.</summary>
    private static Sprite Badge()
    {
        if (badge != null) return badge;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var navy = new Color(0.05f, 0.09f, 0.2f, 0.95f);
        var red = new Color(1f, 0.24f, 0.18f, 1f);
        var cream = new Color(0.97f, 0.95f, 0.88f, 1f);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                Color c = Color.clear;
                if (r <= 1f) c = r > 0.82f ? red : navy;
                bool plus = (Mathf.Abs(u) < 0.16f && Mathf.Abs(v) < 0.56f) || (Mathf.Abs(v) < 0.16f && Mathf.Abs(u) < 0.56f);
                if (plus && r < 0.82f) c = cream;
                // Diagonal slash through the plus.
                if (r < 0.82f && Mathf.Abs(u + v) < 0.17f) c = red;
                float edge = Mathf.Clamp01((1f - r) * size * 0.5f);
                c.a *= edge;
                pixels[y * size + x] = c;
            }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        badge = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return badge;
    }
}
