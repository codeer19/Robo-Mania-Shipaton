using TMPro;
using UnityEngine;

[RequireComponent(typeof(Damageable))]
public class DamageNumberVFX : MonoBehaviour
{
    [Tooltip("Optional authored damage-number prefab. When empty a minimal runtime popup is used as a stand-in.")]
    [SerializeField] private DamageNumberMotion popupPrefab;
    [SerializeField] private TMP_FontAsset popupFont;
    [SerializeField] private Vector3 popupOffset = new Vector3(0f, 2.35f, 0f);
    [SerializeField] private float textScale = 0.045f;
    [SerializeField] private float combineWindow = 0.12f;

    [Header("Readability")]
    [Tooltip("Damage this robot deals to others, shown on the victim.")]
    [SerializeField] private Color dealtColor = new Color(1f, 0.72f, 0.16f, 1f);
    [Tooltip("Damage the local player receives.")]
    [SerializeField] private Color takenColor = new Color(1f, 0.36f, 0.24f, 1f);
    [SerializeField] private float takenScaleBoost = 1.25f;

    private Damageable damageable;
    private FortressTarget identity;
    private int pendingDamage;
    private float showAtTime;
    private bool pendingIsLocalVictim;

    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        identity = GetComponent<FortressTarget>();

        if (popupFont == null)
            popupFont = TMP_Settings.defaultFontAsset;
    }

    private void OnEnable()
    {
        damageable.DamageApplied += OnDamageApplied;
    }

    private void OnDisable()
    {
        damageable.DamageApplied -= OnDamageApplied;
    }

    private void Update()
    {
        if (pendingDamage <= 0 || Time.time < showAtTime)
            return;

        CreateDamageNumber(pendingDamage, pendingIsLocalVictim);
        pendingDamage = 0;
    }

    private void OnDamageApplied(Damageable target, DamageInfo info)
    {
        pendingDamage += info.Amount;

        // Damage the local robot receives has to read differently from damage
        // it deals, otherwise both sides of a fight look identical.
        pendingIsLocalVictim =
            identity == null || identity.Team == FortressTeam.Blue;

        if (showAtTime <= Time.time)
            showAtTime = Time.time + combineWindow;
    }

    private void CreateDamageNumber(int damage, bool isLocalVictim)
    {
        Vector3 randomOffset = new Vector3(
            Random.Range(-0.22f, 0.22f),
            0f,
            Random.Range(-0.08f, 0.08f)
        );

        Vector3 position = transform.position + popupOffset + randomOffset;
        Color color = isLocalVictim ? takenColor : dealtColor;
        float scale = textScale * (isLocalVictim ? takenScaleBoost : 1f);

        if (popupPrefab != null)
        {
            DamageNumberMotion pooled = CombatPool.Get(
                popupPrefab,
                position,
                Quaternion.identity);

            if (pooled != null)
                pooled.Play(damage, color, scale);

            return;
        }

        if (popupFont == null)
        {
            Debug.LogWarning(
                "DamageNumberVFX needs a TMP Font Asset or a popup prefab assigned.",
                this);
            return;
        }

        DamageNumberMotion motion = CombatPool.Get(
            GetRuntimePopupPrefab(),
            position,
            Quaternion.identity);

        if (motion != null)
            motion.Play(damage, color, scale);
    }

    /// <summary>
    /// Minimal reusable popup used until an authored damage-number prefab is
    /// assigned. It is created once and then pooled rather than building a new
    /// GameObject and TextMeshPro for every hit.
    /// </summary>
    private DamageNumberMotion GetRuntimePopupPrefab()
    {
        if (runtimePopupPrefab != null)
            return runtimePopupPrefab;

        GameObject popupObject = new GameObject("Damage Number");
        popupObject.hideFlags = HideFlags.HideAndDontSave;
        popupObject.SetActive(false);

        TextMeshPro text = popupObject.AddComponent<TextMeshPro>();
        text.font = popupFont;
        text.fontSize = 40f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.outlineColor = new Color(0.11f, 0.06f, 0.02f, 1f);
        text.outlineWidth = 0.24f;

        runtimePopupPrefab = popupObject.AddComponent<DamageNumberMotion>();
        return runtimePopupPrefab;
    }

    private static DamageNumberMotion runtimePopupPrefab;
}

public class DamageNumberMotion : MonoBehaviour, IPooledObject
{
    [SerializeField] private TextMeshPro text;
    [SerializeField] private float riseHeight = 0.62f;
    [SerializeField] private float duration = 0.62f;

    private Camera mainCamera;
    private Vector3 startPosition;
    private Color baseColor = Color.white;
    private float scale = 1f;
    private float age;
    private bool playing;

    public void Play(int damage, Color color, float textScale)
    {
        if (text == null)
            text = GetComponent<TextMeshPro>();

        scale = textScale;
        baseColor = color;
        startPosition = transform.position;
        age = 0f;
        playing = true;

        if (text != null)
        {
            text.SetText("{0}", damage);
            text.color = baseColor;
        }

        transform.localScale = Vector3.one * scale;
    }

    public void OnRetrievedFromPool()
    {
        age = 0f;
        playing = false;
    }

    public void OnReturnedToPool()
    {
        playing = false;
    }

    private void Update()
    {
        if (!playing)
            return;

        age += Time.deltaTime;

        float progress = Mathf.Clamp01(age / Mathf.Max(0.05f, duration));

        transform.position = startPosition +
            Vector3.up * (progress * riseHeight);

        float punch = 1f + Mathf.Sin(progress * Mathf.PI) * 0.38f;
        transform.localScale = Vector3.one * scale * punch;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
        {
            transform.rotation = mainCamera.transform.rotation;
        }

        if (text != null)
        {
            Color color = baseColor;
            color.a = 1f - progress;
            text.color = color;
        }

        if (progress >= 1f)
        {
            playing = false;
            CombatPool.Release(gameObject);
        }
    }
}