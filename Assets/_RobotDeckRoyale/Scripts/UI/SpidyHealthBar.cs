using UnityEngine;
using UnityEngine.UI;

/// <summary>Small health-only view; ownership remains on the ground ring.</summary>
public sealed class SpidyHealthBar : MonoBehaviour
{
    private Damageable health;
    private RectTransform fill;
    private Canvas canvas;
    private Camera viewCamera;
    public static void Attach(GameObject bot)
    {
        if (bot.GetComponent<SpidyHealthBar>() == null) bot.AddComponent<SpidyHealthBar>();
    }
    private void Start()
    {
        health = GetComponent<Damageable>(); viewCamera = Camera.main;
        // The generic player HUD adds ammo/name/backing to a tiny Spidy canvas.
        foreach (var old in GetComponentsInChildren<WorldHealthBar>(true))
            if (old.GetComponent<Canvas>() != null) old.gameObject.SetActive(false);
        canvas = new GameObject("Spidy Health",typeof(RectTransform),typeof(Canvas)).GetComponent<Canvas>();
        canvas.transform.SetParent(transform,false); canvas.renderMode=RenderMode.WorldSpace;
        canvas.transform.localPosition = Vector3.up * 1.45f;
        canvas.transform.localScale = Vector3.one * .008f;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(120,10);
        var back = FrontendUI.Panel("Backing",canvas.transform,Vector2.zero,Vector2.one,new Color(.02f,.06f,.04f,.45f),false);
        back.raycastTarget=false;
        var image=FrontendUI.Panel("Green Health",canvas.transform,new Vector2(.015f,.15f),new Vector2(.985f,.85f),new Color(.18f,.96f,.34f),false);
        image.raycastTarget=false; fill=image.rectTransform;
    }
    private void LateUpdate()
    {
        if(canvas==null || health==null) return;
        // Only write what changed: each write re-syncs and re-batches the canvas.
        bool alive=!health.IsDead;
        if(canvas.enabled!=alive) canvas.enabled=alive;
        if(viewCamera==null) viewCamera=Camera.main;
        if(viewCamera!=null && !canvas.transform.rotation.Equals(viewCamera.transform.rotation)) canvas.transform.rotation=viewCamera.transform.rotation;
        var anchorMax=new Vector2(.015f+.97f*Mathf.Clamp01((float)health.CurrentHealth/Mathf.Max(1,health.MaxHealth)),.85f);
        if(fill.anchorMax!=anchorMax) fill.anchorMax=anchorMax;
    }
}
