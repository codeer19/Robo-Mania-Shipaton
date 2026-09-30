using UnityEngine;

public static class PlayerAudioSettings
{
    public static bool PlatformMuted { get; private set; }
    public static float Volume => ProfileStore.GetFloat("RoboMania.Settings.Volume", 1);
    public static bool SfxMuted => ProfileStore.GetInt("RoboMania.Settings.SfxMuted", 0) == 1;
    public static float SfxVolume => SfxMuted ? 0 : ProfileStore.GetFloat("RoboMania.Settings.SfxVolume", 1);
    public static void SetSfxVolume(float value)
    {
        ProfileStore.SetFloat("RoboMania.Settings.SfxVolume", Mathf.Clamp01(value));
        ProfileStore.SetInt("RoboMania.Settings.SfxMuted", value <= 0 ? 1 : 0);
        ProfileStore.Save(); Apply();
    }
    public static void SetVolume(float value)
    {
        ProfileStore.SetFloat("RoboMania.Settings.Volume", Mathf.Clamp01(value));
        ProfileStore.Save(); Apply();
    }
    public static void SetSfxMuted(bool value)
    {
        ProfileStore.SetInt("RoboMania.Settings.SfxMuted", value ? 1 : 0);
        ProfileStore.Save(); Apply();
    }
    public static void SetPlatformMuted(bool value) { PlatformMuted = value; Apply(); }
    public static void Apply()
    {
        AudioListener.volume = PlatformMuted ? 0 : Volume;
        UIAudioManager.SetMuted(SfxMuted);
        UIAudioManager.SetVolume(SfxVolume);
    }
}
