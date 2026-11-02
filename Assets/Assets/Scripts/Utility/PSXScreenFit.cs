using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// level renders into a 16:9 rt shown on a quad, other window shapes showed sky or got cropped.
// resize the rt + quad to the window instead. height stays ps1 low, wider = more fov (capped at ultrawide)
public class PSXScreenFit : MonoBehaviour
{
    private Camera screenCamera;
    private Transform quad;
    private RenderTexture picture;
    private float quadHeight;
    private float shownAspect;

    // bumped when the render height setting changes
    private static int settingsVersion;
    private int shownVersion = -1;
    private int shownHeight;

    public static void Refresh()
    {
        settingsVersion++;
    }

    private const float widestView = 21f / 9f;
    private Camera narrowedCamera;
    private float savedFieldOfView;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded += (scene, mode) => Attach();
        Attach();
    }

    private static void Attach()
    {
        // size by height like 16:9 so the HUD doesn't shrink
        foreach (CanvasScaler scaler in FindObjectsOfType<CanvasScaler>(true))
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
            }
        }
        // backdrops were exactly 16:9, stretch them
        foreach (CanvasScaler scaler in FindObjectsOfType<CanvasScaler>(true))
        {
            Vector2 reference = scaler.referenceResolution;
            foreach (Image image in scaler.GetComponentsInChildren<Image>(true))
            {
                RectTransform rect = image.rectTransform;
                if (rect.anchorMin == rect.anchorMax && Mathf.Abs(rect.rect.width - reference.x) < 2f && Mathf.Abs(rect.rect.height - reference.y) < 2f)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.zero;
                }
            }
        }
        GameObject psxRenderer = GameObject.Find("PSXRenderer");
        if (psxRenderer == null)
        {
            return;
        }
        Camera camera = psxRenderer.GetComponentInChildren<Camera>();
        if (camera != null && camera.GetComponent<PSXScreenFit>() == null)
        {
            camera.gameObject.AddComponent<PSXScreenFit>();
        }
    }

    void Awake()
    {
        screenCamera = GetComponent<Camera>();
        // it only draws the quad: msaa here was ~140 MB of full-window buffers for nothing
        screenCamera.allowMSAA = false;
        MeshRenderer quadRenderer = transform.parent.GetComponentInChildren<MeshRenderer>();
        if (quadRenderer == null)
        {
            return;
        }
        quad = quadRenderer.transform;
        quadHeight = quad.localScale.y;
        // the quad stands in the scene by the start: on its own layer, which no other camera draws, or the world
        // camera saw the picture inside itself (and flickered, drawing into the texture it was reading)
        quad.gameObject.layer = screenLayer;
        // and this camera drew everything too, so chito walking past the quad ended up over the picture.
        // ui stays, the esc menu's canvas is on this camera
        screenCamera.cullingMask = (1 << screenLayer) | (1 << uiLayer);
        picture = quadRenderer.sharedMaterial.mainTexture as RenderTexture;
        // material might call it something else, grab it from the cameras
        foreach (Camera camera in FindObjectsOfType<Camera>(true))
        {
            if (picture == null && camera.targetTexture != null && camera.targetTexture.name == "RT_PSX")
            {
                picture = camera.targetTexture;
            }
        }
    }

    // msaa on the low rt: thin things far off (window bands, beams) are under a pixel and flickered as the view moved.
    // urp only takes the rt's samples when the pipeline asset has msaa on, put back when we go
    private const int samples = 4;
    private UniversalRenderPipelineAsset pipeline;
    private int oldSamples;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += NarrowView;
        RenderPipelineManager.endCameraRendering += RestoreView;
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null)
        {
            oldSamples = pipeline.msaaSampleCount;
            pipeline.msaaSampleCount = samples;
        }
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= NarrowView;
        RenderPipelineManager.endCameraRendering -= RestoreView;
        if (pipeline != null)
        {
            pipeline.msaaSampleCount = oldSamples;
        }
    }

    // free in the tag manager (MapPaper has 31)
    private const int screenLayer = 30;

    private const int uiLayer = 5;

    // cinemachine sets fov every frame, so only narrow it for the render
    private void NarrowView(ScriptableRenderContext context, Camera camera)
    {
        if (camera != screenCamera)
        {
            camera.cullingMask &= ~(1 << screenLayer);
        }
        // with msaa a shader can make a NaN at a thin edge, and bloom blew one pixel up into a white flash over the
        // screen. URP puts black there first
        if (camera.targetTexture == picture && camera.TryGetComponent(out UniversalAdditionalCameraData data))
        {
            data.stopNaN = true;
        }
        if (camera.targetTexture != picture || camera.aspect <= widestView || camera.GetComponent<CinemachineBrain>() == null)
        {
            return;
        }
        narrowedCamera = camera;
        savedFieldOfView = camera.fieldOfView;
        float halfHeight = Mathf.Tan(savedFieldOfView * Mathf.Deg2Rad / 2f) * widestView / camera.aspect;
        camera.fieldOfView = 2f * Mathf.Atan(halfHeight) * Mathf.Rad2Deg;
    }

    private void RestoreView(ScriptableRenderContext context, Camera camera)
    {
        if (camera == narrowedCamera)
        {
            camera.fieldOfView = savedFieldOfView;
            narrowedCamera = null;
        }
    }

    void LateUpdate()
    {
        if (quad == null || picture == null)
        {
            return;
        }
        float windowAspect = (float)Screen.width / Screen.height;
        if (Mathf.Approximately(windowAspect, shownAspect) && shownVersion == settingsVersion && shownHeight == Screen.height)
        {
            return;
        }
        shownAspect = windowAspect;
        shownVersion = settingsVersion;
        shownHeight = Screen.height;

        // never more pixels than the window, very wide windows had a grey strip
        int renderHeight = DisplaySettings.RenderHeight;
        int height = Mathf.Clamp(Screen.height, 1, (renderHeight > 0) ? renderHeight : Screen.height);
        int width = Mathf.Max(1, Mathf.RoundToInt(height * windowAspect));
        if (picture.width != width || picture.height != height || picture.antiAliasing != samples)
        {
            picture.Release();
            picture.width = width;
            picture.height = height;
            picture.antiAliasing = samples;
            picture.Create();
        }
        foreach (Camera camera in FindObjectsOfType<Camera>())
        {
            if (camera.targetTexture == picture)
            {
                camera.aspect = (float)picture.width / picture.height;
            }
        }
        quad.localScale = new Vector3(quadHeight * windowAspect, quadHeight, quad.localScale.z);
        screenCamera.orthographicSize = quad.lossyScale.y / 2f;
    }
}
