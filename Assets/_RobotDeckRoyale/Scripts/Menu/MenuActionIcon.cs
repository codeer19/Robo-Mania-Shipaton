using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu glyphs drawn as UI geometry, never baked into a button's background.
///
/// Every icon is built from the same primitives at the same stroke weight inside
/// the same margin, which is what stops a set of icons reading as clip art
/// collected from different places. Keeping them as generated meshes also means a
/// button is still Image + Icon + TMP_Text rather than one flattened picture.
///
/// CanvasRenderer is required explicitly. Graphic declares it, but the attribute
/// did not carry to this subclass through AddComponent, so every icon in the menu
/// was silently non-renderable and the buttons came out as text only.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MenuActionIcon : MaskableGraphic
{
    [SerializeField] private string kind;

    /// <summary>Which glyph to draw. Assigning rebuilds the mesh.</summary>
    public string Kind
    {
        get => kind;
        set { if (kind == value) return; kind = value; SetVerticesDirty(); }
    }

    private const float Stroke = 0.13f;
    private const float Low = 0.14f;
    private const float High = 0.86f;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        switch (kind)
        {
            case "Video":
                Bar(mesh,.10f,.20f,.90f,.20f);Bar(mesh,.10f,.80f,.90f,.80f);
                Bar(mesh,.10f,.20f,.10f,.80f);Bar(mesh,.90f,.20f,.90f,.80f);
                Triangle(mesh,new Vector2(.37f,.31f),new Vector2(.37f,.69f),new Vector2(.70f,.50f));
                break;
            case "PlayOnline":
                Triangle(mesh, new Vector2(.28f, Low), new Vector2(.28f, High), new Vector2(.84f, .5f));
                break;

            case "Create":
                Bar(mesh, Low, .5f, High, .5f);
                Bar(mesh, .5f, Low, .5f, High);
                break;

            case "Join":
                // Arrow entering a doorway: the mirror of Create's outward plus.
                Bar(mesh, Low, .5f, .58f, .5f);
                Triangle(mesh, new Vector2(.50f, .24f), new Vector2(.50f, .76f), new Vector2(.80f, .5f));
                break;

            case "Shop":
                // Bag: body plus a handle sketched at the same stroke weight.
                Box(mesh, Low, Low, High, .62f);
                Bar(mesh, .32f, .62f, .32f, .78f);
                Bar(mesh, .68f, .62f, .68f, .78f);
                Bar(mesh, .32f, .78f, .68f, .78f);
                break;

            case "Settings":
                Gear(mesh);
                break;

            case "Loadout":
                // A deck: one solid front card, the top and left edges of a
                // second card showing behind it. Outlines alone merged into a
                // blob at button size; a solid shape with a clear gap stays legible.
                Box(mesh, .36f, .12f, .82f, .70f);
                Bar(mesh, .22f, .84f, .68f, .84f);
                Bar(mesh, .22f, .26f, .22f, .84f);
                break;

            default: // Missions: a short checklist.
                for (int row = 0; row < 3; row++)
                {
                    float y = .24f + row * .26f;
                    Box(mesh, Low, y - .055f, Low + .13f, y + .075f);
                    Bar(mesh, .42f, y, High, y);
                }
                break;
        }
    }

    private void Gear(VertexHelper mesh)
    {
        // Drawn as a ring rather than a disc. A solid cog silhouette has no hole,
        // and at button size that reads as a starburst; the hub opening is what
        // makes it legible as a gear.
        const float inner = .14f;
        const float outer = .30f;
        const int teeth = 8;
        Annulus(mesh, inner, outer);
        for (int tooth = 0; tooth < teeth; tooth++)
        {
            float angle = tooth * Mathf.PI * 2f / teeth;
            Vector2 tip = Polar(angle, .46f);
            Vector2 root = Polar(angle, outer - .04f);
            Vector2 side = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * .085f;
            Quad(mesh, root - side, tip - side * .70f, tip + side * .70f, root + side);
        }
    }

    private void Annulus(VertexHelper mesh, float inner, float outer)
    {
        const int segments = 24;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            Quad(mesh, Polar(a, inner), Polar(a, outer), Polar(b, outer), Polar(b, inner));
        }
    }

    private static Vector2 Polar(float angle, float radius) =>
        new Vector2(.5f + Mathf.Cos(angle) * radius, .5f + Mathf.Sin(angle) * radius);

    private void Disc(VertexHelper mesh, float radius)
    {
        const int segments = 20;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            Triangle(mesh, new Vector2(.5f, .5f), Polar(a, radius), Polar(b, radius));
        }
    }

    /// <summary>A stroke of the shared weight between two points.</summary>
    private void Bar(VertexHelper mesh, float x0, float y0, float x1, float y1)
    {
        Vector2 a = new Vector2(x0, y0), b = new Vector2(x1, y1);
        Vector2 normal = Vector2.Perpendicular((b - a).normalized) * (Stroke * .5f);
        Quad(mesh, a - normal, b - normal, b + normal, a + normal);
    }

    private void Box(VertexHelper mesh, float x0, float y0, float x1, float y1) =>
        Quad(mesh, new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1));

    private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        Triangle(mesh, a, b, c);
        Triangle(mesh, a, c, d);
    }

    private void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(Point(a), color, Vector2.zero);
        mesh.AddVert(Point(b), color, Vector2.zero);
        mesh.AddVert(Point(c), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
    }

    /// <summary>Unit-square coordinates mapped into the largest centred square that fits.</summary>
    private Vector3 Point(Vector2 unit)
    {
        Rect area = rectTransform.rect;
        float size = Mathf.Min(area.width, area.height);
        return area.center + (unit - Vector2.one * .5f) * size;
    }
}
