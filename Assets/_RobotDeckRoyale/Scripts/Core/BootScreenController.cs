using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class BootScreenController : MonoBehaviour
{
    private Canvas mainCanvas;
    private RectTransform canvasRect;
    private Camera uiCamera;

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI subtitleText;
    private TextMeshProUGUI tapToContinueText;
    private Image flashOverlay;

    private bool canProceed = false;
    private bool isProceeding = false;

    private class FloatingParticle
    {
        public RectTransform Rect;
        public Image Img;
        public float Speed;
        public float SwaySpeed;
        public float SwayAmount;
        public float InitialX;
        public float TimeOffset;
    }

    private List<FloatingParticle> floatingParticles = new List<FloatingParticle>();

    private void Awake()
    {
        SetupInfrastructure();
        SetupUIElements();
    }

    private IEnumerator Start()
    {
        // 0.0s -> 0.3s
        yield return new WaitForSeconds(0.3f);

        // 5. At 0.3 seconds: Slam in Title
        UITweenEngine.SlamIn(titleText.rectTransform);
        UITweenEngine.ShakeTransform(canvasRect);
        UIAudioManager.Play(UISoundType.PanelOpen);

        // 6. Spawn 8-12 star burst particles
        SpawnBurstParticles(titleText.rectTransform.anchoredPosition);

        // Wait another 0.5s to reach 0.8s
        yield return new WaitForSeconds(0.5f);

        // 7. At 0.8 seconds: Bounce in Subtitle
        UITweenEngine.BounceIn(subtitleText.rectTransform);
        UIAudioManager.Play(UISoundType.TabSwitch);

        // Wait another 0.7s to reach 1.5s
        yield return new WaitForSeconds(0.7f);

        // 8. At 1.5 seconds: Show "TAP TO CONTINUE"
        canProceed = true;

        // 10. Auto-proceed after 4 seconds total (from the start it's 4.0s, we've waited 1.5s so wait 2.5s more)
        yield return new WaitForSeconds(2.5f);

        if (!isProceeding)
        {
            StartCoroutine(ProceedToMainMenu());
        }
    }

    private void Update()
    {
        UpdateFloatingParticles();

        if (canProceed && tapToContinueText != null && !isProceeding)
        {
            // Pulse Alpha
            Color c = tapToContinueText.color;
            // Sine wave between 0.2 and 1.0 roughly
            c.a = 0.6f + Mathf.Sin(Time.time * 5f) * 0.4f;
            tapToContinueText.color = c;

            // Check for input via new Input System.
            bool anyInput = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.anyKey.wasPressedThisFrame;
            if (!anyInput && UnityEngine.InputSystem.Mouse.current != null)
                anyInput = UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
            if (!anyInput && UnityEngine.InputSystem.Touchscreen.current != null)
                anyInput = UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
            if (anyInput)
            {
                StartCoroutine(ProceedToMainMenu());
            }
        }
    }

    private void UpdateFloatingParticles()
    {
        float time = Time.time;
        foreach (var p in floatingParticles)
        {
            Vector2 pos = p.Rect.anchoredPosition;
            pos.y += p.Speed * Time.deltaTime;
            pos.x = p.InitialX + Mathf.Sin((time + p.TimeOffset) * p.SwaySpeed) * p.SwayAmount;
            
            // If it goes above screen, reset to bottom
            if (pos.y > 1080f / 2f + 50f)
            {
                pos.y = -1080f / 2f - 50f;
                p.InitialX = Random.Range(-1920f / 2f, 1920f / 2f);
            }
            p.Rect.anchoredPosition = pos;
        }
    }

    private IEnumerator ProceedToMainMenu()
    {
        isProceeding = true;
        canProceed = false;
        flashOverlay.raycastTarget = true; // Block further clicks

        // 9. Flash white overlay from 0 to 0.8 over 80ms
        float duration = 0.08f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, 0.8f, elapsed / duration);
            flashOverlay.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }
        flashOverlay.color = new Color(1f, 1f, 1f, 0.8f);

        // Load Main Menu
        SceneManager.LoadScene("MainMenu");
    }

    private void SetupInfrastructure()
    {
        // 1. Create Camera
        uiCamera = Camera.main;
        if (uiCamera == null)
        {
            GameObject camObj = new GameObject("BootCamera");
            uiCamera = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            uiCamera.clearFlags = CameraClearFlags.SolidColor;
            uiCamera.backgroundColor = Color.black;
            uiCamera.orthographic = true;
        }

        // The opening UI plays feedback immediately; make sure its camera is
        // audible even when the scene was created without an AudioListener.
        if (FindFirstObjectByType<AudioListener>() == null)
        {
            uiCamera.gameObject.AddComponent<AudioListener>();
        }

        // 1. Create Canvas
        GameObject canvasObj = new GameObject("BootCanvas");
        mainCanvas = canvasObj.AddComponent<Canvas>();
        mainCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        mainCanvas.worldCamera = uiCamera;
        mainCanvas.planeDistance = 5f;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();
        canvasRect = canvasObj.GetComponent<RectTransform>();

        // 1. Create EventSystem
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
        }
    }

    private void SetupUIElements()
    {
        // 2. Background (Navy #0A0E2A)
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasRect, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = GetColor("#0A0E2A");

        // 2. Diagonal Stripe Overlay
        GameObject stripeObj = new GameObject("StripeOverlay");
        stripeObj.transform.SetParent(canvasRect, false);
        RectTransform stripeRect = stripeObj.AddComponent<RectTransform>();
        stripeRect.anchorMin = Vector2.zero;
        stripeRect.anchorMax = Vector2.one;
        stripeRect.sizeDelta = Vector2.zero;
        RawImage stripeImg = stripeObj.AddComponent<RawImage>();
        stripeImg.texture = CreateDiagonalStripeTexture();
        stripeImg.uvRect = new Rect(0, 0, 1920f / 128f, 1080f / 128f);

        // 3. Floating Particles Container
        GameObject particlesObj = new GameObject("FloatingParticles");
        particlesObj.transform.SetParent(canvasRect, false);
        RectTransform pContainer = particlesObj.AddComponent<RectTransform>();
        pContainer.anchorMin = Vector2.zero;
        pContainer.anchorMax = Vector2.one;
        pContainer.sizeDelta = Vector2.zero;
        CreateFloatingParticles(pContainer);

        // 4. Title Text
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(canvasRect, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 80);
        titleRect.localScale = Vector3.zero;

        titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "ROBO MANIA";
        titleText.fontSize = 90;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = GetColor("#FFD52F");
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.characterSpacing = 6;
        titleText.enableWordWrapping = false;
        titleText.overflowMode = TextOverflowModes.Overflow;
        titleText.outlineWidth = 0.3f;
        titleText.outlineColor = GetColor("#0A0E2A");

        // 7. Subtitle Text
        GameObject subtitleObj = new GameObject("Subtitle");
        subtitleObj.transform.SetParent(canvasRect, false);
        RectTransform subtitleRect = subtitleObj.AddComponent<RectTransform>();
        subtitleRect.anchoredPosition = new Vector2(0, -60);
        subtitleRect.localScale = Vector3.zero;

        subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
        subtitleText.text = "FORTRESS DUEL";
        subtitleText.fontSize = 28;
        subtitleText.fontStyle = FontStyles.Bold;
        subtitleText.color = GetColor("#28E5FF");
        subtitleText.alignment = TextAlignmentOptions.Center;
        subtitleText.characterSpacing = 8;
        subtitleText.enableWordWrapping = false;
        subtitleText.overflowMode = TextOverflowModes.Overflow;

        // 8. Tap To Continue Text
        GameObject tapObj = new GameObject("TapToContinue");
        tapObj.transform.SetParent(canvasRect, false);
        RectTransform tapRect = tapObj.AddComponent<RectTransform>();
        tapRect.anchorMin = new Vector2(0.5f, 0);
        tapRect.anchorMax = new Vector2(0.5f, 0);
        tapRect.anchoredPosition = new Vector2(0, 150);

        tapToContinueText = tapObj.AddComponent<TextMeshProUGUI>();
        tapToContinueText.text = "TAP TO CONTINUE";
        tapToContinueText.fontSize = 24;
        tapToContinueText.color = new Color(1, 1, 1, 0);
        tapToContinueText.alignment = TextAlignmentOptions.Center;
        tapToContinueText.enableWordWrapping = false;
        tapToContinueText.overflowMode = TextOverflowModes.Overflow;

        // 9. White Flash Overlay
        GameObject flashObj = new GameObject("FlashOverlay");
        flashObj.transform.SetParent(canvasRect, false);
        RectTransform flashRect = flashObj.AddComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.sizeDelta = Vector2.zero;
        flashOverlay = flashObj.AddComponent<Image>();
        flashOverlay.color = new Color(1, 1, 1, 0);
        flashOverlay.raycastTarget = false;
    }

    private Texture2D CreateDiagonalStripeTexture()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        
        Color clear = new Color(0, 0, 0, 0);
        Color stripeColor = GetColor("#12183A");
        stripeColor.a = 0.3f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Create diagonal stripes 45 degrees
                if ((x + y) % 32 < 16)
                    tex.SetPixel(x, y, stripeColor);
                else
                    tex.SetPixel(x, y, clear);
            }
        }
        tex.Apply();
        return tex;
    }

    private void CreateFloatingParticles(RectTransform container)
    {
        Sprite circleSprite = CreateCircleSprite();
        int count = Random.Range(15, 21);
        Color cyan = GetColor("#28E5FF");
        Color orange = GetColor("#FF7B2C");

        for (int i = 0; i < count; i++)
        {
            GameObject pObj = new GameObject($"FloatParticle_{i}");
            pObj.transform.SetParent(container, false);
            RectTransform pRect = pObj.AddComponent<RectTransform>();
            
            float size = Random.Range(6f, 13f);
            pRect.sizeDelta = new Vector2(size, size);
            
            Image img = pObj.AddComponent<Image>();
            img.sprite = circleSprite;
            
            Color c = Random.value > 0.5f ? cyan : orange;
            c.a = Random.Range(0.2f, 0.4f);
            img.color = c;

            FloatingParticle p = new FloatingParticle
            {
                Rect = pRect,
                Img = img,
                Speed = Random.Range(30f, 80f),
                SwaySpeed = Random.Range(0.5f, 2f),
                SwayAmount = Random.Range(20f, 60f),
                InitialX = Random.Range(-1920f / 2f, 1920f / 2f),
                TimeOffset = Random.Range(0f, 100f)
            };
            
            // Distribute vertically at start
            float startY = Random.Range(-1080f / 2f, 1080f / 2f);
            pRect.anchoredPosition = new Vector2(p.InitialX, startY);
            
            floatingParticles.Add(p);
        }
    }

    private Sprite CreateCircleSprite()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size / 2f;
        Vector2 center = new Vector2(radius, radius);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                // Soft anti-aliased edge
                float alpha = Mathf.Clamp01(radius - dist);
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void SpawnBurstParticles(Vector2 center)
    {
        int count = Random.Range(8, 13);
        for (int i = 0; i < count; i++)
        {
            GameObject pObj = new GameObject($"Burst_{i}");
            pObj.transform.SetParent(titleText.transform.parent, false);
            RectTransform pRect = pObj.AddComponent<RectTransform>();
            pRect.anchoredPosition = center;
            
            TextMeshProUGUI pText = pObj.AddComponent<TextMeshProUGUI>();
            pText.text = Random.value > 0.5f ? "◆" : "★";
            pText.color = Color.white;
            pText.fontSize = 40;
            pText.alignment = TextAlignmentOptions.Center;
            pText.enableWordWrapping = false;
            pText.overflowMode = TextOverflowModes.Overflow;

            StartCoroutine(AnimateBurstParticle(pRect, pText));
        }
    }

    private IEnumerator AnimateBurstParticle(RectTransform rect, TextMeshProUGUI text)
    {
        float duration = 0.4f;
        float elapsed = 0f;
        
        Vector2 dir = Random.insideUnitCircle.normalized;
        float speed = Random.Range(300f, 700f);
        float rotSpeed = Random.Range(-360f, 360f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            rect.anchoredPosition += dir * speed * Time.deltaTime;
            rect.Rotate(0, 0, rotSpeed * Time.deltaTime);
            
            // Decelerate
            speed = Mathf.Lerp(speed, 0, t);
            
            // Fade out
            Color c = text.color;
            c.a = 1f - t;
            text.color = c;
            
            yield return null;
        }
        Destroy(rect.gameObject);
    }

    private Color GetColor(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
            return color;
        return Color.white;
    }
}
