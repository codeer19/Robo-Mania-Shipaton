using UnityEngine;

/// <summary>
/// Gate for per-shot / per-hit combat logging. In release WebGL every Debug.Log
/// is string formatting plus a console write on the main thread, and online
/// combat logged each missile, hit and pickup. Kept on in the Editor, in
/// development builds and whenever network diagnostics are switched on.
/// </summary>
public static class CombatLog
{
    public static bool Verbose => Application.isEditor || Debug.isDebugBuild || NetworkTestHarness.VerboseDiagnostics;
}
