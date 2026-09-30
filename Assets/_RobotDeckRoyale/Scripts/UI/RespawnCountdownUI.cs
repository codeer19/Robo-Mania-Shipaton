using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The existing private-match countdown presentation, shared by all entry modes.</summary>
public sealed class RespawnCountdownUI : MonoBehaviour
{
    private TextMeshProUGUI label;
    private int lastSecond = -1;
    public static RespawnCountdownUI Create(Transform owner)
    {
        var root = new GameObject("Respawn Countdown", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(owner, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 110;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080);
        var view = root.AddComponent<RespawnCountdownUI>();
        view.label = new GameObject("Countdown",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        view.label.transform.SetParent(root.transform,false);
        view.label.rectTransform.sizeDelta = new Vector2(700,240);
        view.label.alignment = TextAlignmentOptions.Center; view.label.fontSize = 54;
        view.label.color = new Color(1,.96f,.84f); view.label.raycastTarget = false;
        view.Show(false,0); return view;
    }
    public void Show(bool visible,int seconds)
    {
        label.gameObject.SetActive(visible);
        if (visible && lastSecond != seconds) { label.text = $"RESPAWNING IN\n<size=90>{seconds}</size>"; lastSecond=seconds; }
        if (!visible) lastSecond=-1;
    }
}
