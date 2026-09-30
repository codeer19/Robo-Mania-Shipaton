using System;
using UnityEngine;

[Serializable]
public sealed class MissionDefinition
{
    public string Id, Title;
    [TextArea] public string Description;
    [Min(1)] public int Target = 1;
    [Min(0)] public int Reward;
    public bool Wins;
    public int Progress => Mathf.Min(Target, Wins ? PlayerProfileService.WonMatches : PlayerProfileService.CompletedMatches);
    public bool Completed => Progress >= Target;
    public bool Claimed => PlayerProfileService.MissionClaimed(Id);
}

[CreateAssetMenu(menuName = "Robo Mania/Mission Catalog")]
public sealed class MissionCatalog : ScriptableObject
{
    public MissionDefinition[] missions = {
        new MissionDefinition { Id="first-match", Title="COMPLETE A MATCH", Target=1, Reward=75 },
        new MissionDefinition { Id="play-three", Title="COMPLETE 3 MATCHES", Target=3, Reward=150 },
        new MissionDefinition { Id="first-win", Title="WIN A MATCH", Target=1, Reward=100, Wins=true }
    };
    private static MissionCatalog current;
    public static MissionDefinition[] Entries
    {
        get
        {
            if (current == null) current = Resources.Load<MissionCatalog>("MissionCatalog");
            if (current == null) current = CreateInstance<MissionCatalog>();
            return current.missions;
        }
    }
    public static MissionDefinition Find(string id) => Array.Find(Entries, entry => entry.Id == id);
}
