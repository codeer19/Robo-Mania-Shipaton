using UnityEngine;

/// <summary>
/// Reports the real-world velocity of a fired projectile, per match type.
///
/// Prefab speed is not evidence. The offline path integrates in Update against
/// Time.deltaTime, the online path integrates in FixedUpdateNetwork against the
/// Fusion tick, and either can diverge from the authored number through a
/// timescale change, a tick-rate mismatch or a stalled simulation. This measures
/// what actually happened: distance travelled over elapsed unscaled real time,
/// which is the quantity a player perceives as "how fast the rocket is".
///
/// Development builds and the Editor only; it observes and never writes.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProjectileSpeedProbe : MonoBehaviour
{
    // Distance is accumulated frame by frame for as long as the projectile is
    // still travelling, and the window closes the moment it stops. Sampling a
    // fixed interval and differencing the endpoints instead charges the flight
    // with the time it spent already dead after an impact, which reads back as
    // roughly half the true speed.
    private const float MaximumWindow = 0.40f;
    // Short enough to still capture a fast rocket's brief flight.
    private const float MinimumWindow = 0.06f;

    private int trackedOnlineId;
    private Vector3 onlineLast;
    private float onlineElapsed, onlineDistance, onlineSpeed;

    private BasicProjectile trackedOffline;
    private Vector3 offlineLast;
    private float offlineElapsed, offlineDistance, offlineSpeed;
    // Scaled vs unscaled time and frame count separate the three ways this can go
    // wrong: a timescale dip, a stalled Update, or the projectile simply being
    // authored slower than it claims.
    private float offlineScaled, offlineMinScale = 1f;
    private int offlineFrames;
    private string offlineName = "?";
    private int offlineStalls, offlineStillRun;
    private float offlinePredicted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // A plain object, deliberately. HideAndDontSave leaves it outside every
        // scene, and an object with no scene never receives Update - the probe
        // silently stopped sampling in some sessions and accumulated a dozen
        // dead copies across Editor runs.
        var host = new GameObject("ProjectileSpeedProbe");
        DontDestroyOnLoad(host);
        host.AddComponent<ProjectileSpeedProbe>();
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void Update()
    {
        SampleOnline();
        SampleOffline();
    }

    private void SampleOnline()
    {
        var state = NetworkedMatchState.Instance;
        if (state == null || state.Object == null || !state.Object.IsValid) { trackedOnlineId = 0; return; }

        int liveIndex = -1;
        for (int i = 0; i < NetworkedMatchState.MissileCapacity; i++)
            if (state.Missiles[i].Id == trackedOnlineId && state.Missiles[i].Active) { liveIndex = i; break; }

        if (liveIndex >= 0)
        {
            var tracked = state.Missiles[liveIndex];
            // No still-frame rule here. Replicated position only changes on a
            // network tick, so at render rates well above the tick rate a live
            // missile sits motionless for several frames at a time - treating
            // that as "stopped" closed the window before it could measure
            // anything. Active/inactive is the honest boundary online.
            onlineDistance += Vector3.Distance(onlineLast, tracked.Position);
            onlineLast = tracked.Position;
            onlineElapsed += Time.unscaledDeltaTime;
            onlineSpeed = tracked.Speed;
            if (onlineElapsed < MaximumWindow) return;
            Close("ONLINE", ref onlineElapsed, ref onlineDistance, onlineSpeed, ref trackedOnlineId);
            return;
        }

        if (trackedOnlineId != 0) Close("ONLINE", ref onlineElapsed, ref onlineDistance, onlineSpeed, ref trackedOnlineId);

        for (int i = 0; i < NetworkedMatchState.MissileCapacity; i++)
        {
            var missile = state.Missiles[i];
            if (!missile.Active || missile.Id == 0) continue;
            trackedOnlineId = missile.Id;
            onlineLast = missile.Position;
            onlineElapsed = 0f;
            onlineDistance = 0f;
            onlineSpeed = missile.Speed;
            Debug.Log($"[MISSILE TRACK] online id={missile.Id} speed={missile.Speed} start={missile.Position:F2}");
            return;
        }
    }

    private void SampleOffline()
    {
        if (trackedOffline != null && trackedOffline.isActiveAndEnabled)
        {
            float step = Vector3.Distance(offlineLast, trackedOffline.transform.position);
            // Stalls are counted but never close the window. Closing on stillness
            // is what corrupted the online figure, and a projectile that has
            // genuinely finished is returned to the pool and deactivated, so
            // active/inactive is the honest boundary on this path too.
            if (step < 0.0005f) offlineStalls++;
            float expectedStep = trackedOffline.Speed * Time.deltaTime;
            // The first frames of a flight, verbatim. If each step is short the
            // fault is in the move itself; if steps are full but sparse the fault
            // is in how often the move runs.
            if (offlineFrames < 6)
                Debug.Log($"[MISSILE STEP] frame={offlineFrames} step={step:F4} expected={expectedStep:F4} " +
                          $"ratio={step / Mathf.Max(0.0001f, expectedStep):F3} dt={Time.deltaTime:F4}");
            offlinePredicted += expectedStep;
            offlineDistance += step;
            offlineLast = trackedOffline.transform.position;
            offlineElapsed += Time.unscaledDeltaTime;
            offlineScaled += Time.deltaTime;
            offlineFrames++;
            offlineMinScale = Mathf.Min(offlineMinScale, Time.timeScale);
            offlineSpeed = trackedOffline.Speed;
            if (offlineElapsed < MaximumWindow) return;
            trackedOffline = null;
            CloseOffline();
            return;
        }

        if (trackedOffline != null) { trackedOffline = null; CloseOffline(); return; }

        foreach (var candidate in FindObjectsByType<BasicProjectile>(FindObjectsSortMode.None))
        {
            if (!candidate.isActiveAndEnabled) continue;
            trackedOffline = candidate;
            offlineLast = candidate.transform.position;
            offlineElapsed = 0f;
            offlineDistance = 0f;
            offlineSpeed = candidate.Speed;
            offlineName = candidate.name;
            return;
        }
    }

    private void CloseOffline()
    {
        if (offlineElapsed >= MinimumWindow)
        {
            Debug.Log($"[MISSILE SPEED] path=OFFLINE match={MatchSessionContext.Type} " +
                      $"measured={offlineDistance / Mathf.Max(0.0001f, offlineElapsed):F2}u/s authored={offlineSpeed:F2}u/s " +
                      $"expectedFromScaledTime={offlineSpeed * offlineScaled / Mathf.Max(0.0001f, offlineElapsed):F2}u/s " +
                      $"distance={offlineDistance:F2} real={offlineElapsed:F3}s scaled={offlineScaled:F3}s " +
                      $"frames={offlineFrames} stalledFrames={offlineStalls} " +
                      $"predictedDistance={offlinePredicted:F2} actualDistance={offlineDistance:F2} " +
                      $"minTimeScale={offlineMinScale:F2} projectile={offlineName}");
        }
        offlineElapsed = 0f; offlineDistance = 0f; offlineScaled = 0f; offlineFrames = 0; offlineMinScale = 1f;
        offlineStalls = 0; offlineStillRun = 0; offlinePredicted = 0f;
    }

    private void Close(string path, ref float elapsed, ref float distance, float authored, ref int tracked)
    {
        if (elapsed >= MinimumWindow) Report(path, distance, elapsed, authored);
        else Debug.Log($"[MISSILE TRACK] {path} id={tracked} discarded: window {elapsed:F3}s < {MinimumWindow:F2}s distance={distance:F2}");
        elapsed = 0f; distance = 0f; tracked = 0;
    }

    private static void Report(string path, float travelled, float elapsed, float authored)
    {
        Debug.Log($"[MISSILE SPEED] path={path} match={MatchSessionContext.Type} entry={MatchSessionContext.EntryMode} " +
                  $"measured={travelled / Mathf.Max(0.0001f, elapsed):F2}u/s authored={authored:F2}u/s " +
                  $"distance={travelled:F2} seconds={elapsed:F3} timeScale={Time.timeScale:F2}");
    }
#endif
}
