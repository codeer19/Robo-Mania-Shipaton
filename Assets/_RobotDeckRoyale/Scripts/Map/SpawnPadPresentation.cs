using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A team spawn pad. Presentation only: the glow ring is tinted for the viewer
/// (your pad cyan, the opponent's red, decided from the authoritative TeamSide),
/// breathes gently, and flashes with a few rising sparks when a robot spawns on
/// it. No gameplay, physics or networking.
/// </summary>
public sealed class SpawnPadPresentation : MonoBehaviour
{
    [SerializeField] private TeamSide side = TeamSide.SideA;
    [SerializeField] private Renderer[] glowRenderers;

    private static readonly List<SpawnPadPresentation> pads = new List<SpawnPadPresentation>();
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock block;
    private ParticleSystem sparks;
    private float flashAt = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => pads.Clear();

    /// <summary>A robot of this side just spawned: flash that side's pad.</summary>
    public static void NotifySpawn(TeamSide spawned)
    {
        for (int i = 0; i < pads.Count; i++)
            if (pads[i] != null && pads[i].side == spawned) pads[i].Flash();
    }

    /// <summary>Match start: both pads.</summary>
    public static void NotifyAll()
    {
        for (int i = 0; i < pads.Count; i++) if (pads[i] != null) pads[i].Flash();
    }

    private void OnEnable() => pads.Add(this);
    private void OnDisable() => pads.Remove(this);

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        sparks = CreateSparks();
    }

    private bool Friendly => MatchSessionContext.Type == MatchType.HumanOnline
        ? TeamSides.IsFriendly(side)
        : side == TeamSide.SideA;

    private void Flash()
    {
        flashAt = Time.time;
        if (sparks == null) return;
        var main = sparks.main;
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, BuildItemPresentation.GlowColour(Friendly));
        sparks.Clear(true);
        sparks.Play(true);
    }

    private void Update()
    {
        float since = Time.time - flashAt;
        float level = 0.75f + 0.12f * Mathf.Sin(Time.time * 2.1f + (side == TeamSide.SideA ? 0f : 1.4f));
        if (since < 0.9f) level += 1.3f * (1f - since / 0.9f);
        Color colour = BuildItemPresentation.GlowColour(Friendly) * level;
        colour.a = 1f;
        for (int i = 0; i < glowRenderers.Length; i++)
        {
            if (glowRenderers[i] == null) continue;
            glowRenderers[i].GetPropertyBlock(block, 1);
            block.SetColor(BaseColorId, colour);
            glowRenderers[i].SetPropertyBlock(block, 1);
        }
    }

    // A dozen short-lived motes rising off the ring: pooled with the pad, no allocation per spawn.
    private ParticleSystem CreateSparks()
    {
        var go = new GameObject("Spawn Sparks");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.2f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.18f);
        main.maxParticles = 16;
        main.gravityModifier = -0.1f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.05f;
        shape.radiusThickness = 0.1f;
        BuildVfx.ConfigureRenderer(go.GetComponent<ParticleSystemRenderer>(), stretched: true);
        return ps;
    }
}
