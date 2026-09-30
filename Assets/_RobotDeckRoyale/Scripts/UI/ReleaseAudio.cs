using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays the authored release SFX through the existing audio boundary.
///
/// This is not a second audio manager: the clips come from the same
/// <see cref="ReleaseAudioCatalog"/> that <see cref="UIAudioManager"/> already
/// reads, master volume stays on AudioListener where PlayerAudioSettings puts
/// it, and SFX volume is applied per play from PlayerAudioSettings so the
/// existing Settings sliders keep working without being rewired.
///
/// One-shots use a small pool of AudioSources rather than a source per emitter,
/// because a wheeled robot, six Spidy legs and a turret volley all firing their
/// own component would cost more voices than a browser build wants to carry.
/// </summary>
public static class ReleaseAudio
{
    private const int PoolSize = 12;

    private static ReleaseAudioCatalog catalog;
    private static bool catalogLoaded;
    private static Transform root;
    private static AudioSource flat;
    private static readonly List<AudioSource> pool = new List<AudioSource>();
    private static int nextVoice;

    public static ReleaseAudioCatalog Catalog
    {
        get
        {
            if (!catalogLoaded)
            {
                catalogLoaded = true;
                catalog = Resources.Load<ReleaseAudioCatalog>("ReleaseAudioCatalog");
                if (catalog == null)
                    Debug.LogWarning("[RELEASE AUDIO] Resources/ReleaseAudioCatalog is missing; SFX stay silent.");
            }
            return catalog;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        catalog = null;
        catalogLoaded = false;
        root = null;
        flat = null;
        pool.Clear();
        nextVoice = 0;
    }

    private static void EnsureVoices()
    {
        if (root != null) return;
        var host = new GameObject("Release Audio");
        Object.DontDestroyOnLoad(host);
        root = host.transform;

        flat = host.AddComponent<AudioSource>();
        flat.playOnAwake = false;
        flat.spatialBlend = 0f;

        for (int index = 0; index < PoolSize; index++)
        {
            var voice = new GameObject("Voice " + index);
            voice.transform.SetParent(root, false);
            var source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            // The listener rides the arena camera, and that camera frames the match
            // from cameraHeight 42 at a 52 degree pitch - so it hangs about 53
            // units from the player for the whole match, not the handful of units
            // the authored `offset` field suggests.
            //
            // This is what made the whole game sound broken. With the old 6/55
            // range every world sound was landing within two units of maxDistance
            // and arriving at roughly a fortieth of its level, while 2D UI and
            // reward sounds came through untouched. Hence turrets and missiles
            // that "had no sound" and a mix that only sounded like UI.
            //
            // Full level out to the camera's own stand-off, then a genuine falloff
            // for things actually further away across the arena.
            source.minDistance = 55f;
            source.maxDistance = 160f;
            pool.Add(source);
        }
    }

    /// <summary>The catalog entry, or null when the slot is deliberately empty.</summary>
    public static ReleaseAudioCatalog.Slot Slot(ReleaseAudioCue cue)
    {
        var current = Catalog;
        var slot = current != null ? current.Find(cue) : null;
        return slot != null && slot.clip != null ? slot : null;
    }

    public static bool Has(ReleaseAudioCue cue) => Slot(cue) != null;

    /// <summary>Non-positional cue: UI, results, reward payouts.</summary>
    public static void Play2D(ReleaseAudioCue cue, float pitch = 1f)
    {
        var slot = Slot(cue);
        if (slot == null || PlayerAudioSettings.SfxMuted) return;
        EnsureVoices();
        flat.pitch = pitch;
        flat.PlayOneShot(slot.clip, slot.volume * PlayerAudioSettings.SfxVolume);
    }

    /// <summary>Positional cue: weapons, impacts, placement, pickups in the arena.</summary>
    public static void PlayAt(ReleaseAudioCue cue, Vector3 position, float pitch = 1f)
    {
        var slot = Slot(cue);
        if (slot == null || PlayerAudioSettings.SfxMuted) return;
        EnsureVoices();

        AudioSource source = pool[nextVoice];
        nextVoice = (nextVoice + 1) % pool.Count;
        source.transform.position = position;
        source.pitch = pitch;
        source.PlayOneShot(slot.clip, slot.volume * PlayerAudioSettings.SfxVolume);
    }
}
