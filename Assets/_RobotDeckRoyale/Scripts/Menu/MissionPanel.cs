using UnityEngine;
using TMPro;

/// <summary>Small local progression screen using the existing match reward and profile boundary.</summary>
public sealed class MissionPanel : MonoBehaviour
{
    private static MissionDefinition[] Entries => MissionCatalog.Entries;
    private Transform cards;
    private bool wasCoolingDown;
    public static void Open(Transform parent)
    {
        if(parent.Find("Missions Screen")!=null) return;
        var back=FrontendUI.Rect("Missions Screen",parent,Vector2.zero,Vector2.one);
        FrontendBackdrop.Create(back);
        var view=back.gameObject.AddComponent<MissionPanel>();
        FrontendUI.Text("Title",back.transform,"MISSIONS",new Vector2(.12f,.81f),new Vector2(.7f,.94f),52,Color.white).alignment=TextAlignmentOptions.Left;
        FrontendUI.Text("Subtitle",back.transform,"PLAY. PROGRESS. EARN COINS.",new Vector2(.12f,.75f),new Vector2(.72f,.82f),21,new Color(.66f,.78f,.9f)).alignment=TextAlignmentOptions.Left;
        FrontendUI.ActionButton("Back",back.transform,"BACK",new Vector2(.77f,.84f),new Vector2(.9f,.93f),
            FrontendUI.Tier.Secondary,null,()=>Destroy(back.gameObject),26f);
        view.cards=FrontendUI.Rect("Entries",back.transform,new Vector2(.12f,.10f),new Vector2(.9f,.73f));
        view.Refresh();
    }
    private void OnEnable() { PlayerProfileService.ProfileChanged+=Refresh; CrazyGamesPlatformService.AvailabilityChanged+=Refresh; }
    private void OnDisable() { PlayerProfileService.ProfileChanged-=Refresh; CrazyGamesPlatformService.AvailabilityChanged-=Refresh; }
    private void Update()
    {
        bool cooling=CrazyGamesPlatformService.RewardedCooldownRemaining>0;
        if(cooling!=wasCoolingDown){wasCoolingDown=cooling;Refresh();}
    }
    private void Refresh()
    {
        if(cards==null) return;
        foreach(Transform child in cards) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        for(int i=0;i<Entries.Length;i++)
        {
            var entry=Entries[i]; float top=1-i*.335f;
            var row=FrontendUI.Panel(entry.Id,cards,new Vector2(0,top-.29f),new Vector2(1,top),FrontendUI.Navy);
            FrontendUI.Text("Mission",row.transform,entry.Title,new Vector2(.035f,.5f),new Vector2(.58f,.88f),29,Color.white).alignment=TextAlignmentOptions.Left;
            FrontendUI.Text("Progress",row.transform,$"{entry.Progress} / {entry.Target}",new Vector2(.035f,.10f),new Vector2(.23f,.42f),23,new Color(.65f,.82f,.96f)).alignment=TextAlignmentOptions.Left;
            FrontendUI.Rect("Coin",row.transform,new Vector2(.47f,.12f),new Vector2(.53f,.44f)).gameObject.AddComponent<FrontendCoinIcon>().raycastTarget=false;
            FrontendUI.Text("Reward",row.transform,"+"+entry.Reward,new Vector2(.53f,.12f),new Vector2(.66f,.44f),25,FrontendUI.Gold);
            bool claimed=PlayerProfileService.MissionClaimed(entry.Id);
            var claim=FrontendUI.ActionButton("Claim",row.transform,claimed?"CLAIMED":"CLAIM",new Vector2(.72f,.25f),new Vector2(.96f,.77f),
                claimed?FrontendUI.Tier.Tertiary:FrontendUI.Tier.Primary,null,
                ()=>{ if(PlayerProfileService.TryClaimMission(entry.Id)) ReleaseAudio.Play2D(ReleaseAudioCue.UiConfirm); },25f);
            claim.interactable=!claimed && entry.Progress>=entry.Target;
            if (CrazyGamesPlatformService.RewardedSupported && !claimed && entry.Completed)
            {
                var claimRect = (RectTransform)claim.transform;
                claimRect.anchorMin = new Vector2(.72f,.53f);
                claimRect.anchorMax = new Vector2(.96f,.93f);
                var doubled = FrontendUI.ActionButton("DoubleReward",row.transform,CrazyGamesPlatformService.RewardedCooldownRemaining>0?"AD COOLDOWN":"2X  +"+(entry.Reward*2),new Vector2(.72f,.07f),new Vector2(.96f,.47f),
                    FrontendUI.Tier.Secondary,"Video",()=>CrazyGamesPlatformService.RequestMissionDouble(entry.Id, granted => {
                        if(granted) ReleaseAudio.Play2D(ReleaseAudioCue.UiConfirm);
                        if(this != null) Refresh(); }),19f);
                doubled.interactable=CrazyGamesPlatformService.RewardedAvailable;
            }
        }
    }
}
