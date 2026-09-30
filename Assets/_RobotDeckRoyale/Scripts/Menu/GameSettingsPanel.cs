using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class GameSettingsPanel
{
    public static GameObject Open(Transform parent, Action back)
    {
        var blocker = FrontendUI.Panel("SettingsOverlay", parent, Vector2.zero, Vector2.one, new Color(.04f,.09f,.18f,.16f),false);
        var frame = FrontendUI.Panel("SettingsFrame", blocker.transform, new Vector2(.27f,.20f),new Vector2(.73f,.80f),FrontendUI.Ink);
        var panel = FrontendUI.Panel("SettingsPanel",frame.transform,new Vector2(.012f,.022f),new Vector2(.988f,.984f),new Color(.08f,.28f,.61f));
        FrontendUI.Text("Heading",panel.transform,"SETTINGS",new Vector2(.07f,.79f),new Vector2(.93f,.96f),40,FrontendUI.Cream).alignment=TextAlignmentOptions.Left;
        FrontendUI.Panel("HeaderLine",panel.transform,new Vector2(.07f,.77f),new Vector2(.93f,.779f),FrontendUI.Gold,false).raycastTarget=false;
        FrontendUI.Text("AudioHeading",panel.transform,"AUDIO",new Vector2(.07f,.66f),new Vector2(.93f,.75f),19,FrontendUI.Gold).alignment=TextAlignmentOptions.Left;
        AddVolume(panel.transform,"MASTER",.48f,PlayerAudioSettings.Volume,PlayerAudioSettings.SetVolume);
        AddVolume(panel.transform,"SFX",.29f,PlayerAudioSettings.SfxVolume,PlayerAudioSettings.SetSfxVolume);
        FrontendUI.ActionButton("Back",panel.transform,"BACK",new Vector2(.32f,.06f),new Vector2(.68f,.21f),FrontendUI.Tier.Primary,null,()=>back(),25);
        return blocker.gameObject;
    }

    private static void AddVolume(Transform parent,string label,float y,float initial,Action<float> changed)
    {
        var row=FrontendUI.Panel(label+"Row",parent,new Vector2(.06f,y),new Vector2(.94f,y+.145f),new Color(.91f,.93f,.86f));
        FrontendUI.Text("Label",row.transform,label,new Vector2(.035f,.15f),new Vector2(.29f,.85f),21,FrontendUI.Ink).alignment=TextAlignmentOptions.Left;
        var value=FrontendUI.Text("Value",row.transform,Mathf.RoundToInt(initial*100)+"%",new Vector2(.84f,.16f),new Vector2(.975f,.84f),20,FrontendUI.Ink);
        var control=FrontendUI.Rect(label+"Slider",row.transform,new Vector2(.34f,.15f),new Vector2(.79f,.85f));
        var track=FrontendUI.Panel("Track",control,new Vector2(0,.37f),new Vector2(1,.63f),FrontendUI.Ink);
        track.raycastTarget=false;
        var fillArea=FrontendUI.Rect("FillArea",control,new Vector2(.025f,.40f),new Vector2(.975f,.60f));
        var fill=FrontendUI.Panel("Fill",fillArea,Vector2.zero,Vector2.one,FrontendUI.Blue,false);fill.raycastTarget=false;
        var handleArea=FrontendUI.Rect("HandleArea",control,new Vector2(.025f,0),new Vector2(.975f,1));
        var handle=FrontendUI.Panel("Handle",handleArea,new Vector2(0,.09f),new Vector2(0,.91f),FrontendUI.Gold);
        handle.rectTransform.sizeDelta=new Vector2(24,0);
        var hit=control.gameObject.AddComponent<Image>();hit.color=Color.clear;
        var slider=control.gameObject.AddComponent<Slider>();
        slider.targetGraphic=handle;slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;
        slider.direction=Slider.Direction.LeftToRight;slider.minValue=0;slider.maxValue=1;slider.SetValueWithoutNotify(initial);
        var colors=slider.colors;colors.normalColor=Color.white;colors.highlightedColor=Color.white;
        colors.pressedColor=new Color(.8f,.85f,.95f);colors.disabledColor=new Color(.55f,.55f,.55f);slider.colors=colors;
        slider.onValueChanged.AddListener(v=>{value.text=Mathf.RoundToInt(v*100)+"%";changed(v);});
    }
}
