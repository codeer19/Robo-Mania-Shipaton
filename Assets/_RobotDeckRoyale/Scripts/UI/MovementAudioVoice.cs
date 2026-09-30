using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The movement loop for one moving thing: the player's chassis, or one Spidy.
///
/// Movement audio is the sound a player hears more than any other, so this
/// deliberately does the quiet things well rather than the loud ones: the loop
/// only runs while the body is actually moving, it fades rather than snapping
/// (a hard stop on a loop is an audible click), and its level is held low
/// enough to sit under combat.
///
/// Spidy is voice limited. A summoned swarm is many bodies running the same
/// short loop, and ten copies of one sample at full level is a wall of noise
/// rather than a swarm, so only the few nearest the listener are audible and the
/// rest stay silent until they are closer.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class MovementAudioVoice : MonoBehaviour
{
    // Raised from 3. At 3, a handful of the player's own Spidys clustered around
    // them took every available voice and the enemy swarm went silent - you could
    // watch red Spidys walk at you and hear nothing. The redesigned loop is much
    // darker and quieter than the one this limit was originally chosen for, so it
    // carries more simultaneous voices without turning into a wall of noise.
    private const int AudibleSpidyLimit = 5;
    private static readonly List<MovementAudioVoice> swarmVoices = new List<MovementAudioVoice>();

    [SerializeField] private ReleaseAudioCue cue = ReleaseAudioCue.RobotMovement;
    [SerializeField] private float baseVolume = 0.5f;
    [SerializeField] private float fadeSeconds = 0.07f;
    [SerializeField] private float movingThreshold = 0.35f;
    [SerializeField] private bool voiceLimited;

    private AudioSource source;
    private Vector3 lastPosition;
    private float speed;
    private float target;
    private bool allowed = true;
    private bool wasMoving;
    private float nextLimitCheck;

    public static MovementAudioVoice Attach(GameObject host, ReleaseAudioCue cue,
        float volume, bool voiceLimited = false)
    {
        var slot = ReleaseAudio.Slot(cue);
        if (slot == null) return null;   // empty slot stays silent by design

        // AddComponent runs Awake and OnEnable synchronously, before any field
        // assigned afterwards is visible to them. Configuring through pending*
        // first is what makes Attach's arguments reach that Awake: setting
        // voice.cue after the call meant every Spidy woke up on the default cue
        // and loaded the *robot* loop, and woke up unlimited, so the swarm never
        // registered for voice limiting either.
        pendingCue = cue;
        pendingVolume = volume;
        pendingVoiceLimited = voiceLimited;
        pendingValid = true;

        var voice = host.AddComponent<MovementAudioVoice>();

        pendingValid = false;
        return voice;
    }

    private static ReleaseAudioCue pendingCue;
    private static float pendingVolume;
    private static bool pendingVoiceLimited;
    private static bool pendingValid;

    private void Awake()
    {
        if (pendingValid)
        {
            cue = pendingCue;
            baseVolume = pendingVolume;
            voiceLimited = pendingVoiceLimited;
        }

        source = GetComponent<AudioSource>();
        var slot = ReleaseAudio.Slot(cue);
        source.clip = slot != null ? slot.clip : null;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        // Matches ReleaseAudio: the listener hangs ~53 units back on the arena
        // camera, so full level has to reach that far or the loop is attenuated to
        // nothing before it is ever heard. Movement rolls off sooner than combat
        // does, so a Spidy across the arena stays in the background.
        source.minDistance = 55f;
        source.maxDistance = 130f;
        // A small random start keeps several bodies from phase-locking into one
        // artificially loud unison hit.
        // Only once decoded: on WebGL a clip's length is unknown until the browser has
        // decoded it, and asking earlier logs a warning per body and returns 0 anyway.
        if (source.clip != null && source.clip.loadState == AudioDataLoadState.Loaded)
            source.time = Random.Range(0f, source.clip.length);
        source.pitch = Random.Range(0.96f, 1.04f);
        lastPosition = transform.position;
    }

    private void OnEnable()
    {
        if (voiceLimited) swarmVoices.Add(this);

        // Started once and then left alone for the whole life of the body.
        //
        // The loop used to be Play()ed whenever the robot started moving and
        // Pause()d when it stopped, but AudioSource.Play() always restarts the
        // clip from sample zero - so every time you touched the movement keys the
        // loop jumped back to its beginning. Stop-starting around the speed
        // threshold made it retrigger over and over, which is the stutter you
        // could hear. Volume alone now decides whether it is audible.
        if (source != null && source.clip != null && !source.isPlaying) source.Play();
    }

    private float silentFor;
    private bool pausedWhileSilent;

    private void OnDisable()
    {
        if (voiceLimited) swarmVoices.Remove(this);
        if (source != null) source.Stop();
        pausedWhileSilent = false;
        silentFor = 0f;
    }

    private void Update()
    {
        if (source == null || source.clip == null) return;

        float delta = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 position = transform.position;
        speed = Mathf.Lerp(speed, (position - lastPosition).magnitude / delta, 12f * delta);
        lastPosition = position;

        if (voiceLimited && Time.unscaledTime >= nextLimitCheck)
        {
            nextLimitCheck = Time.unscaledTime + 0.25f;
            allowed = IsAmongNearest();
        }

        // Hysteresis: start at the threshold, but keep going until well below it.
        // A single threshold chatters while the robot idles against a wall or
        // eases to a halt, and every flicker was a fade-out and fade-in.
        bool moving = speed > (wasMoving ? movingThreshold * 0.55f : movingThreshold);
        wasMoving = moving;
        target = moving && allowed ? baseVolume * PlayerAudioSettings.SfxVolume : 0f;

        // Volume only. The clip keeps rolling underneath whether it is audible or
        // not, so movement fades in and out of a loop that never lost its place.
        source.volume = Mathf.MoveTowards(source.volume, target, delta / Mathf.Max(0.01f, fadeSeconds));

        // A silent loop still costs a spatialised Web Audio source per body. Once
        // it has been fully faded out for a moment it is paused, and UnPause picks
        // it up at the same position - the loop still never restarts from zero.
        if (source.volume <= 0.0001f && target <= 0f)
        {
            silentFor += delta;
            if (!pausedWhileSilent && silentFor > 0.5f && source.isPlaying) { source.Pause(); pausedWhileSilent = true; }
        }
        else
        {
            silentFor = 0f;
            if (pausedWhileSilent) { source.UnPause(); pausedWhileSilent = false; }
        }

        // Subtle speed colouring only. Anything wider reads as a motor revving.
        source.pitch = Mathf.Lerp(source.pitch, Mathf.Clamp(0.96f + speed * 0.012f, 0.96f, 1.06f), 3f * delta);
    }

    private bool IsAmongNearest()
    {
        var listener = Camera.main;
        if (listener == null || swarmVoices.Count <= AudibleSpidyLimit) return true;

        Vector3 ear = listener.transform.position;
        float mine = (transform.position - ear).sqrMagnitude;
        int closer = 0;
        foreach (var other in swarmVoices)
        {
            if (other == null || other == this) continue;
            if ((other.transform.position - ear).sqrMagnitude < mine && ++closer >= AudibleSpidyLimit)
                return false;
        }
        return true;
    }
}
