using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// level renders into a 16:9 rt shown on a quad, other window shapes showed sky or got cropped.
// resize the rt + quad to the window instead. height stays ps1 low, wider = more fov (capped at ultrawide)
public class PSXScreenFit : MonoBehaviour
{
    private Camera screenCamera;
    private Transform quad;
    private RenderTexture picture;
    private int pictureHeight;
    private float quadHeight;
    private float shownAspect;

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
        MeshRenderer quadRenderer = transform.parent.GetComponentInChildren<MeshRenderer>();
        if (quadRenderer == null)
        {
            return;
        }
        quad = quadRenderer.transform;
        quadHeight = quad.localScale.y;
        picture = quadRenderer.sharedMaterial.mainTexture as RenderTexture;
        // material might call it something else, grab it from the cameras
        foreach (Camera camera in FindObjectsOfType<Camera>(true))
        {
            if (picture == null && camera.targetTexture != null && camera.targetTexture.name == "RT_PSX")
            {
                picture = camera.targetTexture;
            }
        }
        if (picture != null)
        {
            pictureHeight = picture.height;
        }
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += NarrowView;
        RenderPipelineManager.endCameraRendering += RestoreView;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= NarrowView;
        RenderPipelineManager.endCameraRendering -= RestoreView;
    }

    // cinemachine sets fov every frame, so only narrow it for the render
    private void NarrowView(ScriptableRenderContext context, Camera camera)
    {
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
        if (Mathf.Approximately(windowAspect, shownAspect))
        {
            return;
        }
        shownAspect = windowAspect;

        // never more pixels than the window, very wide windows had a grey strip
        int height = Mathf.Clamp(Screen.height, 1, pictureHeight);
        int width = Mathf.Max(1, Mathf.RoundToInt(height * windowAspect));
        if (picture.width != width || picture.height != height)
        {
            picture.Release();
            picture.width = width;
            picture.height = height;
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
