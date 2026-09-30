using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DailyBonusView : MonoBehaviour
{
    private Button button;
    private TMP_Text label;
    private float nextRefresh;
    public static void Create(Transform parent)
    {
        var root=FrontendUI.Rect("DailyBonus",parent,new Vector2(.822f,.76f),new Vector2(.978f,.869f));
        var view=root.gameObject.AddComponent<DailyBonusView>();
        view.button=FrontendUI.ActionButton("WatchDailyBonus",root,"+100 COINS",Vector2.zero,Vector2.one,FrontendUI.Tier.Secondary,"Video",
            ()=>CrazyGamesPlatformService.RequestDailyReward(granted=>{
                if(granted) ReleaseAudio.Play2D(ReleaseAudioCue.UiConfirm);
                if(view!=null)view.Refresh();}),21);
        view.label=view.button.GetComponentInChildren<TMP_Text>();
        view.Refresh();
    }
    private void OnEnable()
    {
        PlayerProfileService.ProfileChanged+=Refresh;
        CrazyGamesPlatformService.AvailabilityChanged+=Refresh;
    }
    private void OnDisable()
    {
        PlayerProfileService.ProfileChanged-=Refresh;
        CrazyGamesPlatformService.AvailabilityChanged-=Refresh;
    }
    private void Update() { if(Time.unscaledTime >= nextRefresh) { nextRefresh=Time.unscaledTime+1;Refresh(); } }
    private void Refresh()
    {
        if(button==null)return;
        button.gameObject.SetActive(CrazyGamesPlatformService.RewardedSupported);
        int remaining=CrazyGamesPlatformService.RewardedCooldownRemaining;
        label.text=PlayerProfileService.DailyRewardClaimed?"CLAIMED":remaining>0?"WAIT "+remaining+"s":"+100 COINS";
        button.interactable=!PlayerProfileService.DailyRewardClaimed && CrazyGamesPlatformService.RewardedAvailable;
    }
}
