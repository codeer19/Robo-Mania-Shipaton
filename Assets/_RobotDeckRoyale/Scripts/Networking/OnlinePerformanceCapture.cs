using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
using System;
using System.IO;
#endif

/// <summary>Opt-in acceptance recording. Preallocated samples; serializes only after recording stops.</summary>
public sealed class OnlinePerformanceCapture : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Serializable] private class Report { public string[] markers; public Sample[] frames; }
    [Serializable] private struct Sample { public float deltaMs, rttMs; public long main, gc, physics, simulation, presentation; }
    private readonly Sample[] samples = new Sample[20000];
    private ProfilerRecorder main, gc, physics, simulation, presentation;
    private float until;
    private int count;
    private string output;
    public void Begin(string path)
    {
        output=path; until=Time.unscaledTime+20;
        main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread");
        gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
        physics=ProfilerRecorder.StartNew(ProfilerCategory.Physics,"Physics.Simulate");
        simulation=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Online Combat Simulation");
        presentation=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Online Combat Presentation");
    }
    private void LateUpdate()
    {
        if (output==null) return;
        var state=NetworkedMatchState.Instance;
        if (count<samples.Length) samples[count++]=new Sample { deltaMs=Time.unscaledDeltaTime*1000,
            rttMs=state!=null ? (float)state.Runner.GetPlayerRtt(state.Runner.LocalPlayer)*1000:0,
            main=main.LastValue,gc=gc.LastValue,physics=physics.LastValue,simulation=simulation.LastValue,presentation=presentation.LastValue };
        if(Time.unscaledTime<until && count<samples.Length) return;
        var report=new Report {markers=new[]{"main:ns","gc:bytes","physics:ns","simulation:ns","presentation:ns"},frames=new Sample[count]};
        Array.Copy(samples,report.frames,count);
        Dispose(); File.WriteAllText(output,JsonUtility.ToJson(report)); output=null;
        Destroy(this);
    }
    private void Dispose() { main.Dispose();gc.Dispose();physics.Dispose();simulation.Dispose();presentation.Dispose(); }
    private void OnDestroy() { if(output!=null) Dispose(); }
#else
    public void Begin(string path) { }
#endif
}
