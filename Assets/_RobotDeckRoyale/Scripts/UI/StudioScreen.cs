using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>A static, scene-authored wordmark. No menu assets or services are loaded here.</summary>
public sealed class StudioScreen : MonoBehaviour
{
    [SerializeField, Range(1f, 7f)] private float studioScreenDuration = 5f;
    [SerializeField] private Texture2D loadingArtwork;

    private IEnumerator Start()
    {
        // Start timing after the first rendered frame, including on a cold launch.
        yield return new WaitForEndOfFrame();
        Funnel.Mark("unity_first_frame");

        // The platform SDK initialises underneath the wordmark instead of after it.
        // Its time is mostly spent waiting on the browser (script fetch, portal
        // handshake), so running it during the splash hides up to the splash's full
        // length from every launch. The menu scene is still only loaded once the SDK
        // and profile are ready, exactly as before; it is not pre-loaded with
        // allowSceneActivation=false because a synchronous Resources.Load during the
        // SDK's start-up can deadlock against a paused scene load.
        bool platformReady = false;
        StartCoroutine(InitializePlatform(() => platformReady = true));

#if CRAZYGAMES_BUILD
        yield return new WaitForSecondsRealtime(1f);
#else
        yield return new WaitForSecondsRealtime(studioScreenDuration);
#endif
        // A direct reference to the existing loading image avoids loading the
        // FrontendAssets graph before the loading screen has rendered.
        foreach (var image in GetComponentsInChildren<Image>()) image.gameObject.SetActive(false);
        var loading = FrontendUI.Rect("Existing Loading Screen", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        loading.texture = loadingArtwork;
        loading.raycastTarget = false;
        if (loadingArtwork != null)
        {
            var aspect = loading.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = (float)loadingArtwork.width / loadingArtwork.height;
        }
        var cover = FrontendUI.Panel("Baked Gauge Cover", loading.transform, new Vector2(.026f,.048f), new Vector2(.35f,.15f), FrontendUI.Ink);
        FrontendUI.Text("Loading",cover.transform,"LOADING…",new Vector2(.06f,.12f),new Vector2(.94f,.88f),32,Color.white);
        yield return new WaitForEndOfFrame();
        while (!platformReady) yield return null;
        SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
    }

    private static IEnumerator InitializePlatform(System.Action completed)
    {
        yield return CrazyGamesPlatformService.Initialize();
        completed();
    }
}
