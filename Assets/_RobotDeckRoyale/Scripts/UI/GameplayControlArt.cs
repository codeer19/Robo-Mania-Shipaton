using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The gameplay HUD sprites, including the six build cards. Loaded by the gameplay
/// presenters and by the menu's BUILD LOADOUT screen, so both show the same cards.
/// </summary>
public sealed class GameplayControlArt : ScriptableObject
{
    public Sprite Movement;
    public Sprite Shoot;
    public Sprite Spidy;
    public Sprite Turret;
    public Sprite PulseTower;
    public Sprite HealingPad;
    [FormerlySerializedAs("Mine")] public Sprite OverdrivePad;
    public Sprite RecoveryJammer;
    public Sprite MissileInterceptor;

    public Sprite Card(BuildPlacementController.BuildableType type) => type switch
    {
        BuildPlacementController.BuildableType.Turret => Turret,
        BuildPlacementController.BuildableType.PulseTower => PulseTower,
        BuildPlacementController.BuildableType.HealingPad => HealingPad,
        BuildPlacementController.BuildableType.OverdrivePad => OverdrivePad,
        BuildPlacementController.BuildableType.RecoveryJammer => RecoveryJammer,
        BuildPlacementController.BuildableType.MissileInterceptor => MissileInterceptor,
        _ => null
    };

    private static GameplayControlArt cached;
    public static GameplayControlArt Load() => cached != null ? cached :
        cached = Resources.Load<GameplayControlArt>("UI/GameplayControlArt");
}
