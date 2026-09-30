using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural UI audio system — generates all sounds at runtime via AudioClip.Create().
/// Brawl Stars-inspired: punchy pops, chunky thunks, cartoon snaps.
/// Singleton that persists across scenes.
/// </summary>
public enum UISoundType
{
    ButtonPress,
    ButtonRelease,
    PanelOpen,
    PanelClose,
    TabSwitch,
    CoinCollect,
    ScrapCollect,
    RewardPop,
    Error,
    Hover
}

public class UIAudioManager : MonoBehaviour
{
    private static UIAudioManager _instance;
    private AudioSource _audioSource;
    private Dictionary<UISoundType, AudioClip> _clips = new Dictionary<UISoundType, AudioClip>();

    private static float _volume = 1.0f;
    private static bool _muted = false;

    public static float Volume
    {
        get => _volume;
        set
        {
            _volume = Mathf.Clamp01(value);
            if (_instance != null && _instance._audioSource != null)
            {
                _instance._audioSource.volume = _muted ? 0 : _volume;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_instance == null)
        {
            GameObject go = new GameObject("UIAudioManager");
            _instance = go.AddComponent<UIAudioManager>();
            DontDestroyOnLoad(go);
            _instance.Setup();
        }
    }

    private void Setup()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
        _audioSource.volume = _muted ? 0 : _volume;
#if !CRAZYGAMES_BUILD
        GenerateClips();
#endif
    }

    // One activation must make one sound. Several UI actions raise a button
    // press and an open/close in the same frame - Settings BACK fires
    // ButtonPress and PanelClose together - and two ~150 ms blips at the same
    // instant read as a double click even when they are different clips. The
    // guard is on time rather than on cue for that reason: whichever cue lands
    // first wins the activation, and anything inside the window is dropped.
    // A press a human can actually make is far slower than this window.
    private const float RepeatGuardSeconds = 0.06f;
    private static float _lastCueAt = -1f;

    public static void Play(UISoundType type)
    {
        if (_muted || _instance == null || _instance._audioSource == null)
        {
            return;
        }

#if CRAZYGAMES_BUILD
        var cue = type == UISoundType.PanelClose ? ReleaseAudioCue.UiBack :
            type == UISoundType.RewardPop ? ReleaseAudioCue.UiConfirm : ReleaseAudioCue.UiClick;

        float now = Time.unscaledTime;
        if (_lastCueAt >= 0f && now - _lastCueAt < RepeatGuardSeconds) return;
        _lastCueAt = now;

        // Through ReleaseAudio so the catalog is loaded once and cached, rather
        // than a Resources.Load on every click.
        var slot = ReleaseAudio.Slot(cue);
        if (slot == null)
        {
            // A click that makes no sound is the failure this whole system is for,
            // so it is never allowed to be silent twice.
            Debug.LogWarning("[UI AUDIO] no clip for cue " + cue + " (type " + type + ")");
            return;
        }

        // No _volume here: the AudioSource already carries it, and PlayOneShot
        // multiplies by the source volume, so applying it again would square it.
        _instance._audioSource.PlayOneShot(slot.clip, slot.volume);
#else
        if (_instance._clips.TryGetValue(type, out AudioClip clip))
        {
            _instance._audioSource.PlayOneShot(clip);
        }
#endif
    }

    public static void SetVolume(float volume)
    {
        Volume = volume;
    }

    public static void SetMuted(bool muted)
    {
        _muted = muted;
        if (_instance != null && _instance._audioSource != null)
        {
            _instance._audioSource.volume = _muted ? 0 : _volume;
        }
    }

    private void GenerateClips()
    {
        _clips[UISoundType.ButtonPress] = GenerateButtonPress();
        _clips[UISoundType.ButtonRelease] = GenerateButtonRelease();
        _clips[UISoundType.PanelOpen] = GeneratePanelOpen();
        _clips[UISoundType.PanelClose] = GeneratePanelClose();
        _clips[UISoundType.TabSwitch] = GenerateTabSwitch();
        _clips[UISoundType.CoinCollect] = GenerateCoinCollect();
        _clips[UISoundType.ScrapCollect] = GenerateScrapCollect();
        _clips[UISoundType.RewardPop] = GenerateRewardPop();
        _clips[UISoundType.Error] = GenerateError();
        _clips[UISoundType.Hover] = GenerateHover();
    }

    private AudioClip CreateClip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, 44100, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Chunky pop — noise burst into pitch-down sweep. Satisfying cartoon button press.
    private AudioClip GenerateButtonPress()
    {
        int sampleRate = 44100;
        int length = (int)(0.035f * sampleRate);
        float[] data = new float[length];
        float phase = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float value = 0f;

            if (t < 0.01f)
            {
                value = Random.Range(-1f, 1f) * (1f - t / 0.01f);
            }
            else
            {
                float tSweep = t - 0.01f;
                float freq = Mathf.Lerp(600f, 200f, tSweep / 0.025f);
                phase += 2f * Mathf.PI * freq / sampleRate;
                float env = Mathf.Exp(-tSweep * 150f);
                value = Mathf.Sin(phase) * env;
            }
            data[i] = value * 0.8f;
        }
        return CreateClip("ButtonPress", data);
    }

    // Tiny high-pitched snap-back click.
    private AudioClip GenerateButtonRelease()
    {
        int sampleRate = 44100;
        int length = (int)(0.008f * sampleRate);
        float[] data = new float[length];
        float phase = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            phase += 2f * Mathf.PI * 2000f / sampleRate;

            float env = 1f;
            if (t < 0.002f) env = t / 0.002f;
            else if (t > 0.006f) env = (0.008f - t) / 0.002f;

            data[i] = Mathf.Sin(phase) * env * 0.6f;
        }
        return CreateClip("ButtonRelease", data);
    }

    // Punchy rising whomp — sine sweep + noise burst at start.
    private AudioClip GeneratePanelOpen()
    {
        int sampleRate = 44100;
        int length = (int)(0.08f * sampleRate);
        float[] data = new float[length];
        float phase1 = 0f;
        float phase2 = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float freq = Mathf.Lerp(200f, 900f, t / 0.08f);
            phase1 += 2f * Mathf.PI * freq / sampleRate;
            phase2 += 2f * Mathf.PI * (freq * 2f) / sampleRate;

            float env = 1f;
            if (t < 0.01f) env = t / 0.01f;
            else env = 1f - ((t - 0.01f) / 0.07f);

            float noise = t < 0.01f ? Random.Range(-1f, 1f) * (1f - t / 0.01f) : 0f;

            data[i] = (Mathf.Sin(phase1) * 0.7f + Mathf.Sin(phase2) * 0.3f + noise * 0.2f) * env * 0.8f;
        }
        return CreateClip("PanelOpen", data);
    }

    // Falling sweep with dampened thud.
    private AudioClip GeneratePanelClose()
    {
        int sampleRate = 44100;
        int length = (int)(0.07f * sampleRate);
        float[] data = new float[length];
        float phase1 = 0f;
        float phase2 = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float freq = Mathf.Lerp(700f, 150f, t / 0.07f);
            phase1 += 2f * Mathf.PI * freq / sampleRate;
            phase2 += 2f * Mathf.PI * (freq * 2f) / sampleRate;

            float env = 1f;
            if (t < 0.01f) env = t / 0.01f;
            else env = Mathf.Pow(1f - ((t - 0.01f) / 0.06f), 2f);

            data[i] = (Mathf.Sin(phase1) * 0.7f + Mathf.Sin(phase2) * 0.3f) * env * 0.8f;
        }
        return CreateClip("PanelClose", data);
    }

    // Quick two-tone blip — snappy tab switch.
    private AudioClip GenerateTabSwitch()
    {
        int sampleRate = 44100;
        int length = (int)(0.035f * sampleRate);
        float[] data = new float[length];
        float phase = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            float freq = t < 0.015f ? 1000f : (t >= 0.02f ? 1400f : 0f);

            if (freq > 0f)
            {
                phase += 2f * Mathf.PI * freq / sampleRate;
            }
            else
            {
                phase = 0f;
            }

            float env = 0f;
            if (t < 0.015f) env = 1f - t / 0.015f;
            else if (t >= 0.02f) env = 1f - (t - 0.02f) / 0.015f;

            data[i] = freq > 0f ? Mathf.Sin(phase) * env * 0.7f : 0f;
        }
        return CreateClip("TabSwitch", data);
    }

    // Bright metallic ding — coin clink.
    private AudioClip GenerateCoinCollect()
    {
        int sampleRate = 44100;
        int length = (int)(0.06f * sampleRate);
        float[] data = new float[length];
        float phase1 = 0f;
        float phase2 = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            phase1 += 2f * Mathf.PI * 1400f / sampleRate;
            phase2 += 2f * Mathf.PI * 2100f / sampleRate;

            float env = 1f;
            if (t < 0.005f) env = t / 0.005f;
            else env = Mathf.Exp(-(t - 0.005f) * 60f);

            data[i] = (Mathf.Sin(phase1) * 0.6f + Mathf.Sin(phase2) * 0.4f) * env * 0.8f;
        }
        return CreateClip("CoinCollect", data);
    }

    // Metallic clunk — noise + resonant sines.
    private AudioClip GenerateScrapCollect()
    {
        int sampleRate = 44100;
        int length = (int)(0.04f * sampleRate);
        float[] data = new float[length];
        float phase1 = 0f;
        float phase2 = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            phase1 += 2f * Mathf.PI * 300f / sampleRate;
            phase2 += 2f * Mathf.PI * 600f / sampleRate;

            float env = 1f;
            if (t < 0.005f) env = t / 0.005f;
            else env = Mathf.Exp(-(t - 0.005f) * 80f);

            float noise = Random.Range(-1f, 1f) * 0.5f;

            data[i] = (Mathf.Sin(phase1) * 0.4f + Mathf.Sin(phase2) * 0.3f + noise) * env * 0.7f;
        }
        return CreateClip("ScrapCollect", data);
    }

    // Three ascending tones with sparkle noise — reward fanfare.
    private AudioClip GenerateRewardPop()
    {
        int sampleRate = 44100;
        int length = (int)(0.14f * sampleRate);
        float[] data = new float[length];
        float phase = 0f;
        float lastNoise = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;

            int toneIndex = -1;
            float toneT = 0f;
            float freq = 0f;

            if (t < 0.04f) { toneIndex = 0; toneT = t; freq = 800f; }
            else if (t >= 0.05f && t < 0.09f) { toneIndex = 1; toneT = t - 0.05f; freq = 1000f; }
            else if (t >= 0.1f && t < 0.14f) { toneIndex = 2; toneT = t - 0.1f; freq = 1200f; }

            float toneVal = 0f;
            if (toneIndex >= 0)
            {
                phase += 2f * Mathf.PI * freq / sampleRate;
                float env = 1f;
                if (toneT < 0.005f) env = toneT / 0.005f;
                else env = 1f - (toneT - 0.005f) / 0.035f;
                toneVal = Mathf.Sin(phase) * env;
            }

            float rawNoise = Random.Range(-1f, 1f);
            float hpNoise = rawNoise - lastNoise;
            lastNoise = rawNoise;

            float overallEnv = 1f - (t / 0.14f);

            data[i] = (toneVal * 0.7f + hpNoise * 0.3f * overallEnv) * 0.7f;
        }
        return CreateClip("RewardPop", data);
    }

    // Low chunky buzz — WRONG buzzer.
    private AudioClip GenerateError()
    {
        int sampleRate = 44100;
        int length = (int)(0.15f * sampleRate);
        float[] data = new float[length];
        float phase = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            phase += 2f * Mathf.PI * 80f / sampleRate;

            float val = Mathf.Sign(Mathf.Sin(phase));

            float env = 1f;
            if (t < 0.01f) env = t / 0.01f;
            if (t > 0.14f) env = (0.15f - t) / 0.01f;

            data[i] = val * env * 0.4f;
        }
        return CreateClip("Error", data);
    }

    // Very subtle soft tick — barely perceptible hover feedback.
    private AudioClip GenerateHover()
    {
        int sampleRate = 44100;
        int length = (int)(0.005f * sampleRate);
        float[] data = new float[length];
        float phase = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / sampleRate;
            phase += 2f * Mathf.PI * 3000f / sampleRate;

            float env = 1f;
            if (t < 0.001f) env = t / 0.001f;
            else env = (0.005f - t) / 0.004f;

            data[i] = Mathf.Sin(phase) * env * 0.15f;
        }
        return CreateClip("Hover", data);
    }
}
