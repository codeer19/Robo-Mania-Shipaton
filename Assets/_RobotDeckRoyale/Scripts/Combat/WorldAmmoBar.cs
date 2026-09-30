using UnityEngine;
using UnityEngine.UI;

public class WorldAmmoBar : MonoBehaviour
{
    private const string HolderObjectName = "Ammo Charges";

    [SerializeField, Min(16f)] private float cellWidth = 62f;
    [SerializeField, Min(8f)] private float cellHeight = 18f;
    [SerializeField, Min(0f)] private float cellGap = 7f;
    [SerializeField] private float verticalOffset = -25f;
    [SerializeField] private bool showEnemyAmmo = false;

    private RobotBlaster blaster;
    private FortressBotAI botWeapon;
    private Image[] fills;
    private Image[] glosses;
    private Sprite sourceSprite;
    private RectTransform holder;
    private float emptyPulseUntil;
    private static Sprite fallbackSprite;

    private const float EmptyPulseDuration = 0.28f;

    private readonly Color loadedColor = new Color(1f, 0.58f, 0.12f, 1f);
    private readonly Color emptyColor = new Color(0.06f, 0.09f, 0.17f, 1f);
    private readonly Color rejectedColor = new Color(1f, 0.86f, 0.42f, 1f);

    private void Awake()
    {
        blaster = GetComponentInParent<RobotBlaster>();
        botWeapon = GetComponentInParent<FortressBotAI>();

        // The local player owns RobotBlaster. AI opponents use FortressBotAI;
        // their ammo is deliberately hidden unless a designer opts in.
        bool canDisplay = blaster != null || (showEnemyAmmo && botWeapon != null);

        if (!canDisplay)
        {
            SetExistingHolderVisible(false);
            enabled = false;
            return;
        }

        ResolveSourceSprite();
        CreateOrRefreshAmmoCells();
    }

    private void OnEnable()
    {
        if (blaster != null)
            blaster.FireRejected += HandleFireRejected;
    }

    private void OnDisable()
    {
        if (blaster != null)
            blaster.FireRejected -= HandleFireRejected;
    }

    /// <summary>
    /// A fire request that cannot be served used to disappear silently. Pulsing
    /// the reloading cell tells the player the weapon is empty rather than
    /// letting them believe the input was missed.
    /// </summary>
    private void HandleFireRejected(RobotBlaster source)
    {
        emptyPulseUntil = Time.time + EmptyPulseDuration;
    }

    private void LateUpdate()
    {
        if ((blaster == null && botWeapon == null) || fills == null)
            return;

        int currentAmmo = blaster != null
            ? blaster.CurrentAmmo
            : botWeapon.CurrentAmmo;
        float reloadProgress = blaster != null
            ? blaster.ReloadProgress
            : botWeapon.ReloadProgress;

        float emptyPulse = Mathf.Clamp01(
            (emptyPulseUntil - Time.time) / EmptyPulseDuration);

        for (int index = 0; index < fills.Length; index++)
        {
            float amount = 0f;

            if (index < currentAmmo)
                amount = 1f;
            else if (index == currentAmmo)
                amount = reloadProgress;

            fills[index].fillAmount = amount;

            Color cellColor = Color.Lerp(emptyColor, loadedColor, amount);

            if (emptyPulse > 0f && index == currentAmmo)
                cellColor = Color.Lerp(cellColor, rejectedColor, emptyPulse);

            fills[index].color = cellColor;

            if (glosses[index] != null)
                glosses[index].fillAmount = amount;
        }
    }

    private void ResolveSourceSprite()
    {
        Transform background = transform.Find("Background");

        if (background != null)
        {
            Image backgroundImage = background.GetComponent<Image>();

            if (backgroundImage != null)
                sourceSprite = backgroundImage.sprite;
        }

        // Some Unity installs omit the legacy UISprite resource.
        if (sourceSprite == null)
            sourceSprite = GetFallbackSprite();
    }

    private void CreateOrRefreshAmmoCells()
    {
        int count = blaster != null ? blaster.MaxAmmo : botWeapon.MaxAmmo;
        count = Mathf.Max(1, count);
        fills = new Image[count];
        glosses = new Image[count];
        holder = GetOrCreateRect(transform, HolderObjectName);
        holder.gameObject.SetActive(true);
        holder.SetAsLastSibling();
        holder.anchorMin = new Vector2(0.5f, 0.5f);
        holder.anchorMax = new Vector2(0.5f, 0.5f);
        holder.pivot = new Vector2(0.5f, 0.5f);

        float resolvedVerticalOffset = Mathf.Min(
            verticalOffset,
            WorldHealthBar.AmmoRowVerticalOffset
        );
        holder.anchoredPosition = new Vector2(0f, resolvedVerticalOffset);

        float totalWidth = count * cellWidth + (count - 1) * cellGap;
        holder.sizeDelta = new Vector2(totalWidth, cellHeight);

        for (int index = 0; index < count; index++)
            ConfigureCell(index, totalWidth);

        DisableExtraCells(count);
    }

    private void ConfigureCell(int index, float totalWidth)
    {
        string cellName = "Ammo Cell " + (index + 1);
        RectTransform cell = GetOrCreateImageRect(holder, cellName, out Image background);
        cell.gameObject.SetActive(true);
        cell.anchorMin = new Vector2(0.5f, 0.5f);
        cell.anchorMax = new Vector2(0.5f, 0.5f);
        cell.pivot = new Vector2(0.5f, 0.5f);
        cell.anchoredPosition = new Vector2(
            -totalWidth * 0.5f +
            cellWidth * 0.5f +
            index * (cellWidth + cellGap),
            0f
        );
        cell.sizeDelta = new Vector2(cellWidth, cellHeight);

        background.sprite = sourceSprite;
        background.type = Image.Type.Sliced;
        background.color = emptyColor;
        background.raycastTarget = false;

        UnityEngine.UI.Outline border =
            background.GetComponent<UnityEngine.UI.Outline>();

        if (border == null)
            border = background.gameObject.AddComponent<UnityEngine.UI.Outline>();

        border.effectColor = new Color(0f, 0f, 0f, 0.82f);
        border.effectDistance = new Vector2(2f, -2f);
        border.useGraphicAlpha = true;

        RectTransform fillRect = GetOrCreateImageRect(cell, "Fill", out Image fill);
        fillRect.anchorMin = new Vector2(0.5f, 0.5f);
        fillRect.anchorMax = new Vector2(0.5f, 0.5f);
        fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(cellWidth - 6f, cellHeight - 6f);
        fill.sprite = sourceSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.color = loadedColor;
        fill.raycastTarget = false;

        RectTransform glossRect = GetOrCreateImageRect(fillRect, "Gloss", out Image gloss);
        glossRect.anchorMin = new Vector2(0f, 0.58f);
        glossRect.anchorMax = Vector2.one;
        glossRect.offsetMin = Vector2.zero;
        glossRect.offsetMax = Vector2.zero;
        gloss.sprite = sourceSprite;
        gloss.type = Image.Type.Filled;
        gloss.fillMethod = Image.FillMethod.Horizontal;
        gloss.fillOrigin = 0;
        gloss.color = new Color(1f, 0.92f, 0.65f, 0.32f);
        gloss.raycastTarget = false;

        fills[index] = fill;
        glosses[index] = gloss;
    }

    private void DisableExtraCells(int activeCount)
    {
        for (int index = activeCount; ; index++)
        {
            Transform extra = holder.Find("Ammo Cell " + (index + 1));

            if (extra == null)
                break;

            extra.gameObject.SetActive(false);
        }
    }

    private void SetExistingHolderVisible(bool visible)
    {
        Transform existing = transform.Find(HolderObjectName);

        if (existing != null)
            existing.gameObject.SetActive(visible);
    }

    private static RectTransform GetOrCreateRect(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);

        if (existing != null && existing.TryGetComponent(out RectTransform existingRect))
            return existingRect;

        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static RectTransform GetOrCreateImageRect(
        Transform parent,
        string objectName,
        out Image image)
    {
        Transform existing = parent.Find(objectName);
        GameObject child;

        if (existing != null)
        {
            child = existing.gameObject;
            image = child.GetComponent<Image>();

            if (image == null)
                image = child.AddComponent<Image>();
        }
        else
        {
            child = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            child.transform.SetParent(parent, false);
            image = child.GetComponent<Image>();
        }

        return child.GetComponent<RectTransform>();
    }

    private static Sprite GetFallbackSprite()
    {
        if (fallbackSprite != null)
            return fallbackSprite;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "RuntimeAmmoSprite",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);
        fallbackSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f
        );
        fallbackSprite.name = "RuntimeAmmoSprite";
        return fallbackSprite;
    }
}
