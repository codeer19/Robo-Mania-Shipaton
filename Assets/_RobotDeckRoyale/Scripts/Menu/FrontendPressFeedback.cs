using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class FrontendPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private Vector3 rest;
    private bool pressed;
    private void Start() => rest = transform.localScale;
    public void OnPointerDown(PointerEventData data)
    {
        var button = GetComponent<Button>();
        pressed = button == null || button.IsInteractable();
    }
    public void OnPointerUp(PointerEventData data) => pressed = false;
    public void OnPointerExit(PointerEventData data) => pressed = false;
    private void OnDisable() { pressed = false; if (rest != Vector3.zero) transform.localScale = rest; }
    private void Update()
    {
        if (rest == Vector3.zero) return;
        transform.localScale = Vector3.Lerp(transform.localScale, rest * (pressed ? .96f : 1), 1 - Mathf.Exp(-30 * Time.unscaledDeltaTime));
    }
}

public sealed class FrontendButtonGradient : BaseMeshEffect
{
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        UIVertex v = new UIVertex();
        float low = float.MaxValue, high = float.MinValue;
        for (int i = 0; i < mesh.currentVertCount; i++)
        { mesh.PopulateUIVertex(ref v, i); low = Mathf.Min(low, v.position.y); high = Mathf.Max(high, v.position.y); }
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref v, i);
            Color c = v.color;
            float t = Mathf.InverseLerp(low, high, v.position.y);
            var shaded = Color.Lerp(c * .85f, Color.Lerp(c, Color.white, .16f), t);
            shaded.a = c.a; v.color = shaded;
            mesh.SetUIVertex(v, i);
        }
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class FrontendCoinIcon : Image
{
    protected override void Awake()
    {
        base.Awake(); sprite = Resources.Load<Sprite>("UI/Release/Coin");
        preserveAspect = true; color = Color.white; raycastTarget = false;
    }
}
