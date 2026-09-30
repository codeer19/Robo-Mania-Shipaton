using UnityEngine;

/// <summary>
/// Buildable prefabs do not share a pivot convention: turrets are authored on
/// their base while barriers are centre-pivoted. A single placement height
/// therefore seats one and half-buries the other.
///
/// Snapping by measured renderer bounds is correct for any pivot, and matches
/// how ArenaCombatSetup already seats the arena cover pieces.
/// </summary>
public static class BuildableGrounding
{
    public static void SnapToGround(GameObject buildable, float groundHeight)
    {
        if (buildable == null)
        {
            return;
        }

        Renderer[] renderers = buildable.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds();

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer candidate = renderers[index];

            // Particle and line renderers report whatever they happen to be
            // emitting, which is not a reliable footprint.
            if (candidate == null ||
                candidate is ParticleSystemRenderer ||
                candidate is LineRenderer ||
                !candidate.enabled)
            {
                continue;
            }

            if (!found)
            {
                bounds = candidate.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(candidate.bounds);
            }
        }

        if (!found)
        {
            return;
        }

        float correction = groundHeight - bounds.min.y;
        if (Mathf.Abs(correction) > 0.001f)
        {
            buildable.transform.position += Vector3.up * correction;
        }
    }
}
