using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(FortressTarget))]
public sealed class TeamGroundRing : MonoBehaviour
{
    private const string VisualObjectName = "TeamGroundRing_Visual";
    private const string FillObjectName = "TeamGroundRing_Fill";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Shape")]
    [SerializeField, Min(0.1f)] private float radius = 1.15f;
    [SerializeField, Range(0.02f, 0.5f)] private float ringWidth = 0.3f;
    [SerializeField, Range(16, 128)] private int segments = 64;
    [SerializeField] private float groundOffset = 0.085f;
    [Tooltip("Floor devices (Shock Trap, Repair Pad) are shorter than the robot wheel clearance; their ring sits on the floor.")]
    [SerializeField] private bool floorItem;
    [SerializeField, Range(0.4f, 0.95f)] private float fillRadiusRatio = 0.82f;
    [SerializeField, Range(0f, 0.5f)] private float fillAlpha = 0.16f;

    [Header("Team Colours")]
    [SerializeField, ColorUsage(true, true)]
    private Color blueColour = new Color(0.05f, 0.55f, 2.4f, 1f);

    [SerializeField, ColorUsage(true, true)]
    private Color redColour = new Color(2.6f, 0.08f, 0.04f, 1f);

    [Header("Motion")]
    [SerializeField, Range(0f, 0.35f)] private float pulseAmount = 0.045f;
    [SerializeField, Min(0f)] private float pulseFrequency = 1.35f;

    [Header("Source")]
    [SerializeField] private FortressTarget teamSource;

    private Damageable damageable;
    private Transform visualTransform;
    private Transform fillTransform;
    private Transform outerDiscTransform;
    private Transform innerDiscTransform;
    private LineRenderer ringLine;
    private Mesh ringMesh;
    private Mesh fillMesh;
    private Material ringMaterial;
    private Material fillMaterial;
    private Material outerDiscMaterial;
    private Material innerDiscMaterial;
    private FortressTeam lastTeam;
    private bool hasLastTeam;
    private int builtSegments;
    private float builtRadius;
    private float builtWidth;

    // Per-frame work is kept to what actually changes. The marker rule costs up to
    // three component lookups, its answer only turns to "yes" when a component is
    // added later (a Spidy's health bar), so a "no" is asked again twice a second;
    // the child transforms only move when the shape is reconfigured; visibility is
    // only written when it flips; and the gentle pulse is re-coloured at 20 Hz.
    private const float PulseInterval = 0.05f;
    private const float MarkerRecheckInterval = 0.5f;
    private FortressTarget allowedSource;
    private bool markerAllowed;
    private float nextMarkerCheck = float.NegativeInfinity;
    private bool layoutDirty = true;
    private int shownState = -1;
    private float nextPulseAt;

    public float Radius => radius;

    public static bool AllowsMarker(FortressTarget target) => target != null &&
        (target.TargetType == FortressTargetType.Deployable ||
         target.GetComponent<AutoTurret>() != null || target.GetComponent<SwarmBotAI>() != null ||
         target.GetComponent<SpidyHealthBar>() != null);


    private void Awake()
    {
        damageable = GetComponent<Damageable>();
        ResolveTeamSource();
        EnsureVisual();
        RefreshTeamColour();
    }

    private void OnEnable()
    {
        if (damageable == null)
        {
            damageable = GetComponent<Damageable>();
        }

        EnsureVisual();
        SetMarkerVisible(damageable == null || !damageable.IsDead);
        RefreshTeamColour();
    }

    private void LateUpdate()
    {
        if (teamSource == null)
        {
            ResolveTeamSource();
        }

        EnsureVisual();

        bool shouldShow = damageable == null || !damageable.IsDead;
        SetMarkerVisible(shouldShow);
        if (!shouldShow || shownState != 1)
        {
            return;
        }

        if (teamSource != null && (!hasLastTeam || lastTeam != teamSource.Team))
        {
            RefreshTeamColour();
        }

        if (pulseAmount <= 0f || pulseFrequency <= 0f || Time.time < nextPulseAt)
        {
            return;
        }

        nextPulseAt = Time.time + PulseInterval;
        float pulse = 1f + Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) * pulseAmount;
        ApplyColour(CurrentTeamColour() * pulse);
    }

    private bool MarkerAllowed()
    {
        if (!ReferenceEquals(allowedSource, teamSource))
        {
            allowedSource = teamSource;
            markerAllowed = false;
            nextMarkerCheck = float.NegativeInfinity;
        }

        if (!markerAllowed && Time.unscaledTime >= nextMarkerCheck)
        {
            markerAllowed = AllowsMarker(teamSource);
            nextMarkerCheck = Time.unscaledTime + MarkerRecheckInterval;
        }

        return markerAllowed;
    }

    private void SetMarkerVisible(bool shouldShow)
    {
        shouldShow &= MarkerAllowed();
        int state = shouldShow ? 1 : 0;
        if (shownState == state)
        {
            return;
        }

        shownState = state;
        if (visualTransform != null && visualTransform.gameObject.activeSelf != shouldShow)
        {
            visualTransform.gameObject.SetActive(shouldShow);
        }

        if (fillTransform != null && fillTransform.gameObject.activeSelf != shouldShow)
        {
            fillTransform.gameObject.SetActive(shouldShow);
        }

        if (outerDiscTransform != null && outerDiscTransform.gameObject.activeSelf != shouldShow)
        {
            outerDiscTransform.gameObject.SetActive(shouldShow);
        }

        if (innerDiscTransform != null && innerDiscTransform.gameObject.activeSelf != shouldShow)
        {
            innerDiscTransform.gameObject.SetActive(shouldShow);
        }
    }

    private void OnDisable() => SetMarkerVisible(false);

    private void OnValidate()
    {
        radius = Mathf.Max(0.1f, radius);
        ringWidth = Mathf.Clamp(ringWidth, 0.02f, radius * 0.95f);
        segments = Mathf.Clamp(segments, 16, 128);
        pulseAmount = Mathf.Clamp(pulseAmount, 0f, 0.35f);

        if (Application.isPlaying)
        {
            ResolveTeamSource();
            hasLastTeam = false;
            layoutDirty = true;
        }
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(ringMaterial);
        DestroyRuntimeObject(fillMaterial);
        DestroyRuntimeObject(outerDiscMaterial);
        DestroyRuntimeObject(innerDiscMaterial);
        DestroyRuntimeObject(ringMesh);
        DestroyRuntimeObject(fillMesh);

        if (visualTransform != null)
        {
            DestroyRuntimeObject(visualTransform.gameObject);
        }

        if (fillTransform != null)
        {
            DestroyRuntimeObject(fillTransform.gameObject);
        }

        if (outerDiscTransform != null)
        {
            DestroyRuntimeObject(outerDiscTransform.gameObject);
        }

        if (innerDiscTransform != null)
        {
            DestroyRuntimeObject(innerDiscTransform.gameObject);
        }
    }

    public void SetRadius(float newRadius)
    {
        radius = Mathf.Max(0.1f, newRadius);
        ringWidth = Mathf.Min(ringWidth, radius * 0.95f);
        layoutDirty = true;
        EnsureVisual();
    }

    public void Configure(
        FortressTarget source,
        float configuredRadius,
        float configuredGroundOffset)
    {
        teamSource = source != null ? source : GetComponent<FortressTarget>();
        radius = Mathf.Max(0.1f, configuredRadius);
        ringWidth = Mathf.Min(ringWidth, radius * 0.95f);
        // The imported robot wheels and their contact shadows sit a little
        // above the logical root plane. Keep the marker just beneath those
        // wheels so it cannot be swallowed by the arena floor/shadow pass.
        groundOffset = Mathf.Max(0.42f, configuredGroundOffset);
        hasLastTeam = false;
        layoutDirty = true;
        RefreshVisual();
    }

    public void RefreshVisual()
    {
        ResolveTeamSource();
        EnsureVisual();
        RefreshTeamColour();
    }

    public void RefreshTeamColour()
    {
        if (teamSource != null)
        {
            lastTeam = teamSource.Team;
            hasLastTeam = true;
        }

        ApplyColour(CurrentTeamColour());
    }

    private void ResolveTeamSource()
    {
        if (teamSource == null)
        {
            teamSource = GetComponent<FortressTarget>();
        }
    }

    private void EnsureVisual()
    {
        if (!Application.isPlaying) return;
        ResolveTeamSource();
        if (!MarkerAllowed())
        {
            SetMarkerVisible(false);
            return;
        }

        if (visualTransform == null)
        {
            // New children start active whatever the last written state was.
            shownState = -1;
            layoutDirty = true;
            GameObject visual = new GameObject(VisualObjectName);
            visual.layer = 0;
            visual.transform.SetParent(transform, false);
            visualTransform = visual.transform;

            MeshFilter meshFilter = visual.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = visual.AddComponent<MeshRenderer>();

            ringMesh = new Mesh
            {
                name = name + " Team Ground Ring"
            };
            ringMesh.MarkDynamic();
            meshFilter.sharedMesh = ringMesh;

            ringMaterial = VfxParticleMaterial.ResolveInstance(null, Color.white);

            if (ringMaterial != null)
            {
                ringMaterial.name = name + " Team Ground Ring Material";
                ConfigureTransparentMaterial(ringMaterial);
                meshRenderer.sharedMaterial = ringMaterial;
            }

            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.sortingOrder = 2;
            // The flat mesh can be culled inconsistently by mobile/URP camera
            // combinations. A camera-facing world line keeps the team outline
            // crisp from every supported top-down pitch.
            meshRenderer.enabled = false;
            ringLine = visual.AddComponent<LineRenderer>();
            ringLine.useWorldSpace = false;
            ringLine.loop = true;
            ringLine.alignment = LineAlignment.View;
            ringLine.widthMultiplier = ringWidth;
            ringLine.numCapVertices = 4;
            ringLine.numCornerVertices = 4;
            ringLine.textureMode = LineTextureMode.Stretch;
            ringLine.shadowCastingMode = ShadowCastingMode.Off;
            ringLine.receiveShadows = false;
            ringLine.sortingOrder = 3;
            ringLine.sharedMaterial = ringMaterial;
            ringLine.enabled = true;

            GameObject fill = new GameObject(FillObjectName);
            fill.layer = 0;
            fill.transform.SetParent(transform, false);
            fillTransform = fill.transform;

            MeshFilter fillFilter = fill.AddComponent<MeshFilter>();
            MeshRenderer fillRenderer = fill.AddComponent<MeshRenderer>();
            fillMesh = new Mesh
            {
                name = name + " Team Ground Fill"
            };
            fillFilter.sharedMesh = fillMesh;

            fillMaterial = VfxParticleMaterial.ResolveInstance(null, Color.white);

            if (fillMaterial != null)
            {
                fillMaterial.name = name + " Team Ground Fill Material";
                fillMaterial.renderQueue = 3000;
                ConfigureTransparentMaterial(fillMaterial);
                fillRenderer.sharedMaterial = fillMaterial;
            }

            fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fillRenderer.receiveShadows = false;
            fillRenderer.lightProbeUsage = LightProbeUsage.Off;
            fillRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            fillRenderer.sortingOrder = 1;
            fillRenderer.enabled = false;

            CreateOpaqueGroundMarker();
        }

        if (!layoutDirty &&
            builtSegments == segments &&
            Mathf.Approximately(builtRadius, radius) &&
            Mathf.Approximately(builtWidth, ringWidth))
        {
            return;
        }

        layoutDirty = false;
        float safeGroundOffset = floorItem ? groundOffset : Mathf.Max(0.42f, groundOffset);
        visualTransform.localPosition = Vector3.up * (safeGroundOffset + 0.003f);
        visualTransform.localRotation = Quaternion.identity;
        visualTransform.localScale = Vector3.one;
        if (fillTransform != null)
        {
            fillTransform.localPosition = Vector3.up * safeGroundOffset;
            fillTransform.localRotation = Quaternion.identity;
            fillTransform.localScale = Vector3.one;
        }
        UpdateGroundMarkerTransform(safeGroundOffset);

        if (ringMesh != null &&
            (builtSegments != segments ||
             !Mathf.Approximately(builtRadius, radius) ||
             !Mathf.Approximately(builtWidth, ringWidth)))
        {
            RebuildMesh();
        }
    }

    private void RebuildMesh()
    {
        int safeSegments = Mathf.Clamp(segments, 16, 128);
        float outerRadius = Mathf.Max(0.1f, radius);
        float innerRadius = Mathf.Max(0.01f, outerRadius - ringWidth);

        Vector3[] vertices = new Vector3[safeSegments * 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[safeSegments * 6];

        for (int index = 0; index < safeSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / safeSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertex = index * 2;

            vertices[vertex] = new Vector3(x * innerRadius, 0f, z * innerRadius);
            vertices[vertex + 1] = new Vector3(x * outerRadius, 0f, z * outerRadius);
            normals[vertex] = Vector3.up;
            normals[vertex + 1] = Vector3.up;
            uvs[vertex] = new Vector2((float)index / safeSegments, 0f);
            uvs[vertex + 1] = new Vector2((float)index / safeSegments, 1f);

            int nextVertex = ((index + 1) % safeSegments) * 2;
            int triangle = index * 6;

            // Winding faces upward so the ring remains inexpensive and single-sided.
            triangles[triangle] = vertex;
            triangles[triangle + 1] = nextVertex;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = nextVertex;
            triangles[triangle + 5] = nextVertex + 1;
        }

        ringMesh.Clear();
        ringMesh.vertices = vertices;
        ringMesh.normals = normals;
        ringMesh.uv = uvs;
        ringMesh.triangles = triangles;
        ringMesh.RecalculateBounds();

        if (ringLine != null)
        {
            ringLine.positionCount = safeSegments;
            ringLine.widthMultiplier = ringWidth;

            for (int index = 0; index < safeSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / safeSegments;
                ringLine.SetPosition(index, new Vector3(
                    Mathf.Cos(angle) * outerRadius,
                    0f,
                    Mathf.Sin(angle) * outerRadius));
            }
        }

        if (fillMesh != null)
        {
            float fillRadius = outerRadius * fillRadiusRatio;
            Vector3[] fillVertices = new Vector3[safeSegments + 1];
            Vector3[] fillNormals = new Vector3[safeSegments + 1];
            Vector2[] fillUvs = new Vector2[safeSegments + 1];
            int[] fillTriangles = new int[safeSegments * 3];
            fillNormals[0] = Vector3.up;
            fillUvs[0] = new Vector2(0.5f, 0.5f);

            for (int index = 0; index < safeSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / safeSegments;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                int vertex = index + 1;
                fillVertices[vertex] = new Vector3(x * fillRadius, 0f, z * fillRadius);
                fillNormals[vertex] = Vector3.up;
                fillUvs[vertex] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);

                int triangle = index * 3;
                fillTriangles[triangle] = 0;
                fillTriangles[triangle + 1] = ((index + 1) % safeSegments) + 1;
                fillTriangles[triangle + 2] = vertex;
            }

            fillMesh.Clear();
            fillMesh.vertices = fillVertices;
            fillMesh.normals = fillNormals;
            fillMesh.uv = fillUvs;
            fillMesh.triangles = fillTriangles;
            fillMesh.RecalculateBounds();
        }

        builtSegments = safeSegments;
        builtRadius = radius;
        builtWidth = ringWidth;
    }

    private Color CurrentTeamColour()
    {
        FortressTeam team = teamSource != null ? teamSource.Team : FortressTeam.Blue;
        if (MatchSessionContext.Type == MatchType.HumanOnline)
            return TeamSides.IsFriendly(TeamSides.FromSceneTeam(team)) ? blueColour : redColour;
        return team == FortressTeam.Red ? redColour : blueColour;
    }

    private void ApplyColour(Color colour)
    {
        if (ringMaterial == null && fillMaterial == null)
        {
            return;
        }

        Color ringColour = colour;
        ringColour.a = 0.92f;
        Color centreColour = colour;
        centreColour.a = fillAlpha;
        Color outerFillColour = new Color(
            Mathf.Clamp01(colour.r),
            Mathf.Clamp01(colour.g),
            Mathf.Clamp01(colour.b),
            0.28f);
        Color innerFillColour = new Color(
            Mathf.Clamp01(colour.r),
            Mathf.Clamp01(colour.g),
            Mathf.Clamp01(colour.b),
            0.10f);

        if (ringLine != null)
        {
            ringLine.startColor = ringColour;
            ringLine.endColor = ringColour;
        }

        if (ringMaterial != null && ringMaterial.HasProperty(BaseColorId))
        {
            ringMaterial.SetColor(BaseColorId, ringColour);
        }

        if (ringMaterial != null && ringMaterial.HasProperty(ColorId))
        {
            ringMaterial.SetColor(ColorId, ringColour);
        }

        if (fillMaterial != null && fillMaterial.HasProperty(BaseColorId))
        {
            fillMaterial.SetColor(BaseColorId, centreColour);
        }

        if (fillMaterial != null && fillMaterial.HasProperty(ColorId))
        {
            fillMaterial.SetColor(ColorId, centreColour);
        }

        SetMaterialColour(outerDiscMaterial, outerFillColour);
        SetMaterialColour(innerDiscMaterial, innerFillColour);
    }

    private static void DestroyRuntimeObject(Object runtimeObject)
    {
        if (runtimeObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeObject);
        }
        else
        {
            DestroyImmediate(runtimeObject);
        }
    }

    internal static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
            return;

        material.renderQueue = 3100;
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private void CreateOpaqueGroundMarker()
    {
        // Taken from the shipped Resources material rather than Shader.Find so
        // the marker resolves in a player build instead of drawing magenta.
        Material template = VfxParticleMaterial.ResolveUnlit(null);

        if (template == null)
            return;

        Shader shader = template.shader;

        outerDiscTransform = CreateDisc(
            "TeamGroundMarker_Outer",
            shader,
            out outerDiscMaterial);
        innerDiscTransform = CreateDisc(
            "TeamGroundMarker_Inner",
            shader,
            out innerDiscMaterial);
        SetMaterialColour(innerDiscMaterial, new Color(0.05f, 0.55f, 1f, 0.1f));
    }

    private Transform CreateDisc(
        string objectName,
        Shader shader,
        out Material material)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = objectName;
        // Robot roots use gameplay/collision layers that are intentionally
        // excluded by some arena cameras.  Keep the ground marker on the
        // visible Default layer so it renders consistently in Game view and
        // Android builds without changing any gameplay-layer membership.
        disc.layer = 0;
        disc.transform.SetParent(transform, false);

        Collider collider = disc.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        MeshRenderer renderer = disc.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.sortingOrder = 4;

        material = new Material(shader)
        {
            name = name + " " + objectName + " Material",
            hideFlags = HideFlags.DontSave,
            renderQueue = 3300
        };
        ConfigureMarkerMaterial(material);
        renderer.sharedMaterial = material;
        return disc.transform;
    }

    private void UpdateGroundMarkerTransform(float safeGroundOffset)
    {
        if (outerDiscTransform != null)
        {
            outerDiscTransform.localPosition =
                Vector3.up * (safeGroundOffset + 0.025f);
            outerDiscTransform.localRotation = Quaternion.identity;
            outerDiscTransform.localScale = new Vector3(
                radius * 2.12f,
                0.018f,
                radius * 2.12f);
        }

        if (innerDiscTransform != null)
        {
            float innerRadius = Mathf.Max(
                0.05f,
                radius - ringWidth * 1.35f);
            innerDiscTransform.localPosition =
                Vector3.up * (safeGroundOffset + 0.065f);
            innerDiscTransform.localRotation = Quaternion.identity;
            innerDiscTransform.localScale = new Vector3(
                innerRadius * 2f,
                0.018f,
                innerRadius * 2f);
        }
    }

    private static void ConfigureMarkerMaterial(Material material)
    {
        if (material == null)
            return;

        material.renderQueue = 3300;
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat(
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.enableInstancing = true;
    }

    private static void SetMaterialColour(Material material, Color colour)
    {
        if (material == null)
            return;

        material.color = colour;
        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, colour);
        if (material.HasProperty(ColorId))
            material.SetColor(ColorId, colour);
    }
}
