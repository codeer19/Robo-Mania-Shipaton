using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BUILD LOADOUT: choose the three build cards taken into every match.
///
/// Tap an equipped slot, then tap a card to put it there. A card that is already
/// equipped swaps places instead of appearing twice, so the loadout is always
/// exactly three different cards and never empty. Saved straight away through
/// <see cref="PlayerProfileService"/>, the same profile as coins and skins.
/// Built with the same FrontendUI pieces as the Missions and Shop screens.
/// </summary>
public sealed class LoadoutPanel : MonoBehaviour
{
    private const string ScreenName = "Loadout Screen";
    private static readonly Color SlotFace = new Color(.07f, .15f, .29f);
    private static readonly Color Muted = new Color(.66f, .78f, .9f);

    private RectTransform slotsRoot, cardsRoot;
    private TextMeshProUGUI detailName, detailText;
    private int selectedSlot;
    private BuildPlacementController.BuildableType focused;

    public static void Open(Transform parent)
    {
        if (parent.Find(ScreenName) != null) return;
        var back = FrontendUI.Rect(ScreenName, parent, Vector2.zero, Vector2.one);
        FrontendBackdrop.Create(back);
        var view = back.gameObject.AddComponent<LoadoutPanel>();

        FrontendUI.Text("Title", back.transform, "BUILD LOADOUT", new Vector2(.06f, .855f), new Vector2(.62f, .955f), 50, Color.white).alignment = TextAlignmentOptions.Left;
        FrontendUI.Text("Subtitle", back.transform, "PICK 3 CARDS TO TAKE INTO EVERY MATCH", new Vector2(.06f, .805f), new Vector2(.72f, .86f), 21, Muted).alignment = TextAlignmentOptions.Left;
        FrontendUI.ActionButton("Back", back.transform, "BACK", new Vector2(.835f, .855f), new Vector2(.955f, .945f),
            FrontendUI.Tier.Secondary, null, () => { UIAudioManager.Play(UISoundType.PanelClose); Destroy(back.gameObject); }, 26f);

        FrontendUI.Text("EquippedLabel", back.transform, "EQUIPPED", new Vector2(.06f, .72f), new Vector2(.46f, .78f), 26, FrontendUI.Gold).alignment = TextAlignmentOptions.Left;
        view.slotsRoot = FrontendUI.Rect("Slots", back.transform, new Vector2(.06f, .30f), new Vector2(.46f, .715f));

        FrontendUI.Text("AllLabel", back.transform, "ALL BUILD CARDS", new Vector2(.51f, .72f), new Vector2(.95f, .78f), 26, FrontendUI.Cream).alignment = TextAlignmentOptions.Left;
        view.cardsRoot = FrontendUI.Rect("AllCards", back.transform, new Vector2(.51f, .08f), new Vector2(.955f, .715f));

        var detail = FrontendUI.Panel("Detail", back.transform, new Vector2(.06f, .08f), new Vector2(.46f, .27f), FrontendUI.Ink);
        FrontendUI.Inset("Face", detail.transform, FrontendUI.Navy, 5f, 5f, 5f, 5f).raycastTarget = false;
        detail.raycastTarget = false;
        view.detailName = FrontendUI.Text("Name", detail.transform, "", new Vector2(.05f, .52f), new Vector2(.95f, .92f), 30, Color.white);
        view.detailName.alignment = TextAlignmentOptions.Left;
        view.detailText = FrontendUI.Text("Description", detail.transform, "", new Vector2(.05f, .1f), new Vector2(.95f, .52f), 22, Muted);
        view.detailText.alignment = TextAlignmentOptions.TopLeft;

        var loadout = PlayerProfileService.BuildLoadout;
        view.focused = loadout[0];
        view.Rebuild();
        UIAudioManager.Play(UISoundType.PanelOpen);
    }

    private void OnEnable() => PlayerProfileService.ProfileChanged += Rebuild;
    private void OnDisable() => PlayerProfileService.ProfileChanged -= Rebuild;

    private void Rebuild()
    {
        if (slotsRoot == null || cardsRoot == null) return;
        var art = GameplayControlArt.Load();
        var loadout = PlayerProfileService.BuildLoadout;
        Clear(slotsRoot);
        Clear(cardsRoot);

        // Three equipped slots, large enough to tap on a phone.
        for (int i = 0; i < loadout.Length; i++)
        {
            int slot = i;
            float left = i / 3f;
            var cell = FrontendUI.Rect("Slot" + (i + 1), slotsRoot, new Vector2(left + .012f, 0f), new Vector2(left + 1f / 3f - .012f, 1f));
            bool selected = slot == selectedSlot;
            AddCard(cell, art != null ? art.Card(loadout[i]) : null, loadout[i], selected, false,
                () => { selectedSlot = slot; focused = loadout[slot]; Rebuild(); });
            var number = FrontendUI.Panel("SlotNumber", cell, new Vector2(.36f, -.02f), new Vector2(.64f, .09f), selected ? FrontendUI.Gold : FrontendUI.Ink);
            number.raycastTarget = false;
            FrontendUI.Text("Value", number.transform, (i + 1).ToString(), Vector2.zero, Vector2.one, 22, selected ? FrontendUI.Ink : FrontendUI.Cream);
        }

        // All six, 3 x 2.
        var all = BuildCards.All;
        for (int i = 0; i < all.Length; i++)
        {
            var type = all[i];
            int column = i % 3, row = i / 3;
            float left = column / 3f, top = 1f - row / 2f;
            var cell = FrontendUI.Rect(type.ToString(), cardsRoot, new Vector2(left + .01f, top - .5f + .015f), new Vector2(left + 1f / 3f - .01f, top - .015f));
            bool equipped = BuildCards.Contains(loadout, type);
            AddCard(cell, art != null ? art.Card(type) : null, type, type == focused && !equipped, equipped, () => Equip(type));
        }

        detailName.text = BuildCards.DisplayName(focused);
        detailText.text = BuildCards.Description(focused);
    }

    /// <summary>Puts a card in the selected slot, swapping if it is already equipped elsewhere.</summary>
    private void Equip(BuildPlacementController.BuildableType type)
    {
        focused = type;
        var loadout = PlayerProfileService.BuildLoadout;
        int existing = System.Array.IndexOf(loadout, type);
        if (existing == selectedSlot) { Rebuild(); return; }
        if (existing >= 0) loadout[existing] = loadout[selectedSlot];
        loadout[selectedSlot] = type;
        if (PlayerProfileService.TrySetBuildLoadout(loadout))
        {
            ReleaseAudio.Play2D(ReleaseAudioCue.UiConfirm);
            selectedSlot = (selectedSlot + 1) % BuildCards.LoadoutSize;
        }
        Rebuild();
    }

    private static void AddCard(RectTransform cell, Sprite sprite, BuildPlacementController.BuildableType type, bool highlighted, bool equipped, UnityEngine.Events.UnityAction onTap)
    {
        // The card keeps the art's own proportions inside the area above the
        // ribbon row. FitInParent fills its parent, so the reserved row has to be
        // a separate parent rather than the card's own anchors.
        var area = FrontendUI.Rect("Area", cell, new Vector2(0f, .1f), Vector2.one);
        var holder = FrontendUI.Rect("Card", area, Vector2.zero, Vector2.one);
        var fitter = holder.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = MobileBuildHUD.CardAspect;

        if (highlighted || equipped)
        {
            var ring = FrontendUI.Panel("Selected", holder, Vector2.zero, Vector2.one, highlighted ? FrontendUI.Gold : new Color(.30f, .80f, .97f, .9f));
            ring.rectTransform.offsetMin = new Vector2(-6f, -6f);
            ring.rectTransform.offsetMax = new Vector2(6f, 6f);
            ring.raycastTarget = false;
        }

        var image = new GameObject("Art", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(holder, false);
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        image.sprite = sprite != null ? sprite : FrontendUI.Solid;
        image.color = sprite != null ? Color.white : SlotFace;
        image.preserveAspect = true;
        if (sprite == null)
            FrontendUI.Text("Name", holder, BuildCards.DisplayName(type), new Vector2(.05f, .4f), new Vector2(.95f, .6f), 18, Color.white);

        var button = holder.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => UIAudioManager.Play(UISoundType.ButtonPress));
        button.onClick.AddListener(onTap);
        holder.gameObject.AddComponent<FrontendPressFeedback>();

        if (equipped)
        {
            // Under the card, not over the render.
            var ribbon = FrontendUI.Panel("Equipped", cell, new Vector2(.12f, 0f), new Vector2(.88f, .085f), FrontendUI.Cyan);
            ribbon.raycastTarget = false;
            FrontendUI.Text("Label", ribbon.transform, "EQUIPPED", Vector2.zero, Vector2.one, 16, FrontendUI.Ink);
        }
    }

    private static void Clear(Transform root)
    {
        foreach (Transform child in root) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
    }
}
