using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Compact home-screen status under the pilot profile: Daily Battles progress and
/// the 7-day streak. No popup, no timers, no red badges - the streak row simply
/// becomes a CLAIM button on a day that has not been claimed, and otherwise shows
/// what tomorrow is worth. Also lights a small gold dot on MISSIONS while a
/// mission reward is waiting.
/// </summary>
public sealed class DailyStatusView : MonoBehaviour
{
    private readonly Image[] pips = new Image[DailyProgress.DailyBattlesTarget];
    private TMP_Text battlesValue, streakLabel, streakAction;
    private Image streakFace;
    private Button streakButton;
    private GameObject missionDot;
    private float nextRefresh;

    public static void Create(RectTransform safeRoot)
    {
        var panel = FrontendUI.Panel("DailyStatus", safeRoot, new Vector2(.020f, .728f), new Vector2(.248f, .850f), FrontendUI.Ink);
        panel.raycastTarget = false;
        var view = panel.gameObject.AddComponent<DailyStatusView>();
        view.Build(panel.transform, safeRoot);
    }

    private void Build(Transform panel, RectTransform safeRoot)
    {
        var title = FrontendUI.Text("BattlesLabel", panel, "DAILY BATTLES", new Vector2(.065f, .56f), new Vector2(.52f, .94f), 20f, FrontendUI.Cream);
        title.alignment = TextAlignmentOptions.Left;
        for (int i = 0; i < pips.Length; i++)
        {
            float left = .54f + i * .1f;
            pips[i] = FrontendUI.Panel("Battle " + (i + 1), panel, new Vector2(left, .64f), new Vector2(left + .075f, .86f), FrontendUI.Gold);
            pips[i].raycastTarget = false;
        }
        battlesValue = FrontendUI.Text("BattlesValue", panel, "0/3", new Vector2(.84f, .56f), new Vector2(.96f, .94f), 20f, FrontendUI.Gold);

        var row = FrontendUI.Rect("Streak", panel, new Vector2(.04f, .08f), new Vector2(.96f, .50f));
        streakFace = row.gameObject.AddComponent<Image>();
        streakFace.sprite = FrontendUI.Solid;
        streakFace.color = new Color(.078f, .16f, .30f);
        streakButton = row.gameObject.AddComponent<Button>();
        streakButton.targetGraphic = streakFace;
        streakButton.onClick.AddListener(Claim);
        streakLabel = FrontendUI.Text("StreakLabel", row, "", new Vector2(.03f, .05f), new Vector2(.5f, .95f), 19f, FrontendUI.Cream);
        streakLabel.alignment = TextAlignmentOptions.Left;
        streakAction = FrontendUI.Text("StreakAction", row, "", new Vector2(.48f, .05f), new Vector2(.98f, .95f), 17f, FrontendUI.Gold);
        streakAction.alignment = TextAlignmentOptions.Right;

        Transform missions = safeRoot.Find("Missions");
        if (missions != null)
        {
            var dot = FrontendUI.Panel("Ready", missions, new Vector2(.86f, .68f), new Vector2(.97f, .98f), FrontendUI.Gold);
            dot.raycastTarget = false;
            missionDot = dot.gameObject;
        }
        Refresh();
    }

    private void OnEnable() => PlayerProfileService.ProfileChanged += Refresh;
    private void OnDisable() => PlayerProfileService.ProfileChanged -= Refresh;

    // Catches the UTC day rolling over while the menu is open.
    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 2f;
        Refresh();
    }

    private void Claim()
    {
        if (CrazyGamesPlatformService.InteractionBlocked) return;
        if (!DailyProgress.TryClaimStreak(out _, out int coins, out int xp)) return;
        ReleaseAudio.Play2D(ReleaseAudioCue.UiConfirm);
        streakAction.text = "+" + coins + (xp > 0 ? "  +" + xp + " XP" : "");
        nextRefresh = Time.unscaledTime + 2.5f;   // let the payout read before the row updates
    }

    private void Refresh()
    {
        if (battlesValue == null) return;
        int battles = Mathf.Min(DailyProgress.DailyBattlesTarget, DailyProgress.DailyBattlesToday);
        battlesValue.text = battles + "/" + DailyProgress.DailyBattlesTarget;
        for (int i = 0; i < pips.Length; i++)
            pips[i].color = i < battles ? FrontendUI.Gold : new Color(.12f, .18f, .32f);

        if (DailyProgress.StreakClaimable)
        {
            int day = DailyProgress.NextStreakDay;
            streakLabel.text = "STREAK DAY " + day;
            streakAction.text = "CLAIM +" + DailyProgress.StreakRewardCoins(day);
            streakFace.color = new Color(.12f, .43f, .86f);
            streakButton.interactable = true;
        }
        else
        {
            int day = Mathf.Max(1, DailyProgress.CurrentStreakDay);
            int next = DailyProgress.NextStreakDay;
            streakLabel.text = "STREAK DAY " + day;
            streakAction.text = "TOMORROW +" + DailyProgress.StreakRewardCoins(next);
            streakFace.color = new Color(.078f, .16f, .30f);
            streakButton.interactable = false;
        }

        if (missionDot != null)
        {
            bool ready = false;
            foreach (var mission in MissionCatalog.Entries)
                if (mission.Completed && !mission.Claimed) { ready = true; break; }
            missionDot.SetActive(ready);
        }
    }
}
