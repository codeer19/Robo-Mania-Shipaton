using System;
using UnityEngine;

public enum ReleaseAudioCue
{
    UiClick, UiBack, UiConfirm, RobotMovement, MissileFire, MissileImpact,
    TurretDeploy, TurretFire, TurretHit, SpidyMove, SpidyAttack, SpidyHit, SpidyDeath,
    ECellPickup, EnergyReady, PlayerHit, PlayerDestruction, Respawn,
    HeistHit, HeistDestruction, Victory, Defeat,
    // Appended: serialized slots store the cue as an int, so new cues only go at the end.
    // OverdriveBoost reuses the retired Mine blast slot (value 24).
    ShockZap, PulseTower, OverdriveBoost, Intercept, HealPulse
}

[CreateAssetMenu(menuName = "Robo Mania/Release Audio Slots")]
public sealed class ReleaseAudioCatalog : ScriptableObject
{
    [Serializable] public sealed class Slot
    {
        public ReleaseAudioCue cue;
        public AudioClip clip;
        [Range(0,1)] public float volume = 1;
    }
    [Tooltip("Only assign selected final clips. Empty slots intentionally remain silent.")]
    public Slot[] slots = CreateSlots();
    private static Slot[] CreateSlots()
    {
        var cues = (ReleaseAudioCue[])Enum.GetValues(typeof(ReleaseAudioCue));
        var result = new Slot[cues.Length];
        for (int i = 0; i < cues.Length; i++) result[i] = new Slot { cue = cues[i] };
        return result;
    }
    public Slot Find(ReleaseAudioCue cue) => Array.Find(slots, slot => slot.cue == cue);
}
