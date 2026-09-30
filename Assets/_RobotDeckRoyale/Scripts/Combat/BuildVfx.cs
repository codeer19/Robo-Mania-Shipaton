using UnityEngine;

/// <summary>
/// Build-card effects, built from code with the project's existing additive
/// particle material (no new shaders, no textures, no lights). Every burst is a
/// pooled template reused through <see cref="CombatPool"/>, so repeated
/// activations never Instantiate/Destroy:
///   Pulse - Pulse Tower ring,  Boost - Overdrive Pad burst ring,
///   Jam   - Recovery Jammer interference,  Beam - Missile Interceptor shot.
/// </summary>
public static class BuildVfx
{
    private static GameObject zapTemplate, ringTemplate, beamTemplate;
    public static readonly Color Friendly = new Color(0.16f, 0.9f, 1f, 1f);
    public static readonly Color Enemy = new Color(1f, 0.28f, 0.14f, 1f);
    /// <summary>Overdrive amber: speed, distinct from heal green, jammer red and pulse violet.</summary>
    public static readonly Color BoostColour = new Color(1f, 0.66f, 0.12f, 1f);

    /// <summary>Soft ring that expands to the tower's radius.</summary>
    public static void Pulse(Vector3 position, float radius, Color color) =>
        PlayRing(position, radius, color, 0.5f, 0.22f);

    /// <summary>Quick amber ring across the pad when it boosts a robot.</summary>
    public static void Boost(Vector3 position, float radius) =>
        PlayRing(position, radius, BoostColour, 0.3f, 0.22f);

    /// <summary>Interference at the jammer and on the robot it caught.</summary>
    public static void Jam(Vector3 position, float radius, Vector3 victim, Color color)
    {
        PlayZap(position, radius, color, false);
        PlayZap(victim, 0.9f, color, true);
        ReleaseAudio.PlayAt(ReleaseAudioCue.ShockZap, position, Random.Range(0.86f, 0.94f));
    }

    /// <summary>Short tracer from the interceptor's head to the missile it destroyed.</summary>
    public static void Beam(Vector3 from, Vector3 to, Color color)
    {
        if (beamTemplate == null) beamTemplate = BuildTemplate<BeamBurst>("Intercept Beam", b => b.Build());
        var beam = Spawn(beamTemplate, from, 0.35f);
        if (beam != null) beam.GetComponent<BeamBurst>().Begin(from, to, color);
    }

    private static void PlayRing(Vector3 position, float radius, Color color, float duration, float width)
    {
        if (ringTemplate == null) ringTemplate = BuildTemplate<RingBurst>("Pulse Ring", r => r.Build());
        var ring = Spawn(ringTemplate, position, duration + 0.1f);
        if (ring != null) ring.GetComponent<RingBurst>().Begin(radius, color, duration, width);
    }

    private static void PlayZap(Vector3 position, float radius, Color color, bool small)
    {
        if (zapTemplate == null) zapTemplate = BuildTemplate<ShockZapBurst>("Interference Zap", z => z.Build());
        var zap = Spawn(zapTemplate, position, 0.6f);
        if (zap != null) zap.GetComponent<ShockZapBurst>().Begin(radius, color, small);
    }

    private static GameObject Spawn(GameObject template, Vector3 position, float lifetime)
    {
        GameObject instance = CombatPool.Get(template, position, Quaternion.identity);
        if (instance == null) return null;
        var pooled = instance.GetComponent<PooledEffect>();
        if (pooled == null) { pooled = instance.AddComponent<PooledEffect>(); CombatPool.RefreshParts(instance); }
        pooled.Play(lifetime);
        return instance;
    }

    public static ParticleSystem CreateStatusSparks(Transform parent)
    {
        var go = new GameObject("Slow Status Sparks");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * 1.0f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Friendly);
        main.maxParticles = 12;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = 26f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.55f;
        ConfigureRenderer(go.GetComponent<ParticleSystemRenderer>(), stretched: true);
        return ps;
    }

    /// <summary>
    /// The amber streak a boosted robot leaves on the ground. A trail renderer
    /// rather than particles: small additive sparks disappeared against the warm
    /// floor from the gameplay camera, while one tapering alpha-blended ribbon
    /// reads as speed at a glance. A dozen vertices, drawn only while the robot
    /// moves; one shared material.
    /// </summary>
    public static BoostTrail CreateBoostTrail(Transform parent)
    {
        var go = new GameObject("Overdrive Trail");
        go.transform.SetParent(parent, false);
        var trail = go.AddComponent<BoostTrail>();
        trail.Build(BoostTrailMaterial());
        return trail;
    }

    private static Material boostTrailMaterial;

    private static Material BoostTrailMaterial()
    {
        if (boostTrailMaterial != null) return boostTrailMaterial;
        boostTrailMaterial = VfxParticleMaterial.ResolveInstance(null, Color.white);
        if (boostTrailMaterial == null) return null;
        boostTrailMaterial.name = "Overdrive Trail";
        TeamGroundRing.ConfigureTransparentMaterial(boostTrailMaterial);
        return boostTrailMaterial;
    }

    private static GameObject BuildTemplate<T>(string name, System.Action<T> build) where T : Component
    {
        var holder = new GameObject("~BuildVfxTemplate " + name);
        holder.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(holder);
        holder.SetActive(false);
        var root = new GameObject(name);
        root.transform.SetParent(holder.transform, false);
        build(root.AddComponent<T>());
        return root;
    }

    internal static LineRenderer Line(Transform parent, string name, int points, float width, bool loop, bool worldSpace = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = points;
        line.loop = loop;
        line.useWorldSpace = worldSpace;
        line.widthMultiplier = width;
        line.numCapVertices = 1;
        line.sharedMaterial = VfxParticleMaterial.Resolve(null);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    internal static ParticleSystem Sparks(Transform parent, int count, float speed)
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(parent, false);
        var sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.maxParticles = count + 2;
        main.gravityModifier = 0.6f;
        var emission = sparks.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.3f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        ConfigureRenderer(go.GetComponent<ParticleSystemRenderer>(), stretched: true);
        return sparks;
    }

    internal static void ConfigureRenderer(ParticleSystemRenderer renderer, bool stretched)
    {
        renderer.sharedMaterial = VfxParticleMaterial.Resolve(null);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (stretched)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 1.6f;
        }
    }
}

/// <summary>An expanding ground ring (outer and inner line) that fades out.</summary>
public sealed class RingBurst : MonoBehaviour
{
    private const int Points = 40;
    // Serialized so pooled clones point at their own children (Instantiate copies
    // only serialized fields).
    [SerializeField] private LineRenderer outer;
    [SerializeField] private LineRenderer inner;
    private float startedAt, radius, duration, width;
    private Color colour;

    internal void Build()
    {
        outer = BuildVfx.Line(transform, "Outer", Points, 0.2f, true);
        inner = BuildVfx.Line(transform, "Inner", Points, 0.1f, true);
    }

    public void Begin(float ringRadius, Color ringColour, float ringDuration, float ringWidth)
    {
        if (outer == null || inner == null) return;
        radius = Mathf.Max(0.5f, ringRadius);
        colour = ringColour;
        duration = Mathf.Max(0.1f, ringDuration);
        width = ringWidth;
        startedAt = Time.time;
        Tick();
    }

    private void Update() => Tick();

    private void Tick()
    {
        if (outer == null || inner == null) return;
        float t = Mathf.Clamp01((Time.time - startedAt) / duration);
        float ease = 1f - (1f - t) * (1f - t);
        Color c = colour; c.a = 1f - t * t;
        outer.startColor = outer.endColor = c;
        c.a *= 0.55f;
        inner.startColor = inner.endColor = c;
        outer.widthMultiplier = Mathf.Lerp(width, width * 0.4f, t);
        inner.widthMultiplier = outer.widthMultiplier * 0.6f;
        SetCircle(outer, Mathf.Lerp(0.4f, radius, ease));
        SetCircle(inner, Mathf.Lerp(0.3f, radius * 0.72f, ease));
    }

    private static void SetCircle(LineRenderer line, float r)
    {
        for (int i = 0; i < Points; i++)
        {
            float a = i * Mathf.PI * 2f / Points;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0.14f, Mathf.Sin(a) * r));
        }
    }
}

/// <summary>A tracer line plus a spark pop where the missile was destroyed.</summary>
public sealed class BeamBurst : MonoBehaviour
{
    private const float Duration = 0.18f;
    [SerializeField] private LineRenderer beam;
    [SerializeField] private ParticleSystem sparks;
    private float startedAt;
    private Color colour;

    internal void Build()
    {
        beam = BuildVfx.Line(transform, "Beam", 2, 0.14f, false, worldSpace: true);
        sparks = BuildVfx.Sparks(transform, 12, 5f);
    }

    public void Begin(Vector3 from, Vector3 to, Color beamColour)
    {
        if (beam == null || sparks == null) return;
        colour = beamColour;
        startedAt = Time.time;
        beam.SetPosition(0, from);
        beam.SetPosition(1, to);
        sparks.transform.position = to;
        var main = sparks.main;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, colour);
        sparks.Clear(true);
        sparks.Play(true);
        Tick();
    }

    private void Update() => Tick();

    private void Tick()
    {
        if (beam == null) return;
        float t = Mathf.Clamp01((Time.time - startedAt) / Duration);
        Color c = Color.Lerp(Color.white, colour, 0.35f); c.a = 1f - t;
        beam.startColor = c;
        c.a *= 0.6f;
        beam.endColor = c;
        beam.widthMultiplier = Mathf.Lerp(0.16f, 0.04f, t);
    }
}

/// <summary>Interference zap: an expanding ring, a few flickering arcs and a spark burst.</summary>
public sealed class ShockZapBurst : MonoBehaviour
{
    private const int RingPoints = 28;
    private const int Arcs = 4;
    private const int ArcPoints = 6;
    private const float Duration = 0.32f;

    [SerializeField] private LineRenderer ring;
    [SerializeField] private LineRenderer[] arcs = new LineRenderer[Arcs];
    [SerializeField] private ParticleSystem sparks;
    private float startedAt, radius, nextArcShuffle;
    private bool small;
    private Color colour;

    internal void Build()
    {
        ring = BuildVfx.Line(transform, "Ring", RingPoints, 0.1f, true);
        for (int i = 0; i < Arcs; i++) arcs[i] = BuildVfx.Line(transform, "Arc", ArcPoints, 0.05f, false);
        sparks = BuildVfx.Sparks(transform, 16, 6f);
    }

    public void Begin(float zapRadius, Color zapColour, bool smallZap)
    {
        if (ring == null || sparks == null || arcs == null || arcs.Length != Arcs) return;
        small = smallZap;
        radius = Mathf.Max(0.5f, zapRadius);
        colour = zapColour;
        startedAt = Time.time;
        nextArcShuffle = 0f;
        var main = sparks.main;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, colour);
        sparks.Clear(true);
        sparks.Play(true);
        Tick();
    }

    private void Update() => Tick();

    private void Tick()
    {
        if (ring == null || arcs == null) return;
        float t = Mathf.Clamp01((Time.time - startedAt) / Duration);
        float r = small ? Mathf.Lerp(radius, 0.35f, t) : Mathf.Lerp(0.35f, radius, 1f - (1f - t) * (1f - t));
        Color c = colour; c.a = 1f - t;
        ring.startColor = ring.endColor = c;
        float height = small ? 1.1f : 0.12f;
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i * Mathf.PI * 2f / RingPoints;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, height, Mathf.Sin(a) * r));
        }
        bool arcsOn = t < 0.7f;
        if (arcsOn && Time.time >= nextArcShuffle)
        {
            nextArcShuffle = Time.time + 0.045f;
            for (int i = 0; i < Arcs; i++) ShuffleArc(arcs[i], i);
        }
        Color arcColour = Color.Lerp(Color.white, colour, 0.5f); arcColour.a = arcsOn ? 1f - t : 0f;
        for (int i = 0; i < Arcs; i++) arcs[i].startColor = arcs[i].endColor = arcColour;
    }

    // A jagged line from the core out toward the ring.
    private void ShuffleArc(LineRenderer arc, int index)
    {
        float angle = (index + Random.value * 0.8f) * Mathf.PI * 2f / Arcs;
        float reach = small ? radius * 0.8f : radius * 0.9f;
        Vector3 end = new Vector3(Mathf.Cos(angle) * reach, small ? 0.6f : 0.15f, Mathf.Sin(angle) * reach);
        Vector3 start = new Vector3(0f, small ? 1.6f : 1.2f, 0f);
        for (int p = 0; p < ArcPoints; p++)
        {
            float k = p / (float)(ArcPoints - 1);
            Vector3 point = Vector3.Lerp(start, end, k);
            if (p > 0 && p < ArcPoints - 1)
                point += new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(-0.08f, 0.12f), Random.Range(-0.18f, 0.18f));
            arc.SetPosition(p, point);
        }
    }
}

/// <summary>
/// The Overdrive streak under a boosted robot. Emits only while the boost runs;
/// a jump (respawn, teleport) wipes it so it never draws a line across the map.
/// </summary>
public sealed class BoostTrail : MonoBehaviour
{
    [SerializeField] private TrailRenderer streak;
    private Vector3 lastPosition;

    internal void Build(Material material)
    {
        transform.localPosition = Vector3.up * 0.12f;
        streak = gameObject.AddComponent<TrailRenderer>();
        streak.sharedMaterial = material;
        streak.time = 0.45f;
        streak.minVertexDistance = 0.2f;
        streak.widthMultiplier = 0.95f;
        streak.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.45f));
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.97f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.58f, 0.06f), 0.35f), new GradientColorKey(new Color(1f, 0.5f, 0.04f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });
        streak.colorGradient = gradient;
        streak.numCapVertices = 2;
        streak.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streak.receiveShadows = false;
        streak.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        streak.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        streak.emitting = false;
        enabled = false;
    }

    public void SetEmitting(bool on)
    {
        if (streak == null) return;
        if (on && !streak.emitting) { streak.Clear(); lastPosition = transform.position; }
        streak.emitting = on;
        enabled = on;
    }

    public void Clear()
    {
        if (streak != null) streak.Clear();
    }

    private void LateUpdate()
    {
        Vector3 now = transform.position;
        if ((now - lastPosition).sqrMagnitude > 4f) streak.Clear();
        lastPosition = now;
    }
}
