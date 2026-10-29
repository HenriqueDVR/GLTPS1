using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
public class PlayerMapController : MonoBehaviour
{
    [SerializeField] private PlayerInputInitialize playerInputScript;

    private InputContextStack.Token holding;
    public Node node;
    [SerializeField] private NodeData nodeDataScript;
    [SerializeField] private Camera mapCamera;
    [SerializeField] private RenderTexture renderTexture;
    [SerializeField] private RawImage[] mapImage;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip mapSFX;
    [SerializeField] private float duration = 0.2f;
    public bool isMapEnabled;
    private float elasped;

    public StartEnvironmentTrace startEnvironmentTraceScript;

    // where the map cam was when the map got drawn, for markers
    public Matrix4x4 RenderedViewProjection { get; private set; }
    public RawImage MapImage => mapImage[0];

    // our last map picture, freed when the next one is taken
    private Texture2D mapPicture;

    void Awake()
    {
        // only drawn on demand in RenderMap, enabled it drew the whole map every frame
        mapCamera.enabled = false;
    }

    void OnEnable()
    {
        playerInputScript.actions.Player.Map.performed += Map;
        playerInputScript.actions.Vehicle.Map.performed += Map;
    }

    void OnDisable()
    {
        playerInputScript.actions.Player.Map.performed -= Map;
        playerInputScript.actions.Vehicle.Map.performed -= Map;
    }

    private void Map(InputAction.CallbackContext context)
    {
        if (context.performed && Time.timeScale == 1)
        {
            // driver needs hands off the controls
            if (!isMapEnabled && (DriverSteering() || GetComponent<PlayerCameraController>().equipCamera))
            {
                return;
            }
            if (!isMapEnabled)
            {
                if (holding == null)
                {
                    // no switching girls with the map in hand
                    holding = playerInputScript.Contexts.Push(InputContext.HoldingMap, "map");
                }

                //Time.timeScale = 0;
                StartCoroutine(RenderMap(0f));
                MapPaper.Open(this);

                nodeDataScript.drawPath = true;

                // reset flags
                nodeDataScript.isFirstIntersectionNode = false;

                if (nodeDataScript.edgesFromPlayer.Count > 0)
                {
                    foreach (GenerateNode edge in nodeDataScript.edgesFromPlayer)
                    {
                        edge.newNode.GetComponent<Node>().tracePathFromPlayerNode = false;
                        edge.newNode.GetComponent<Node>().isIntersectionNodeSet = false;

                    }
                }
                nodeDataScript.edgesFromPlayer = new List<GenerateNode>();
                nodeDataScript.points = new List<Transform>();

                // if player is not on pillar path node, trigger trace path from player node
                if (!node.tracePathFromPillarNode)
                {
                    startEnvironmentTraceScript.triggerTraceFromPlayerNode = true;
                }
                // if player is on pillar path node, set current node player is on as intersect node for recalculating sub branches
                else
                {
                    node.tracePathFromPlayerNode = true;
                    node.isIntersectionNodeSet = false;
                    nodeDataScript.istracePathFromPlayerNodeComplete = true;
                }

                audioSource.Stop();
                audioSource.PlayOneShot(mapSFX);

                StartCoroutine(RenderMap(0.3f));

                isMapEnabled = true;
                elasped = 0;
            }
            else
            {
                CloseMap();
            }
        }
    }

    public void CloseMap()
    {
        holding?.Release();
        holding = null;

        Time.timeScale = 1;
        isMapEnabled = false;
        elasped = 0;
        MapPaper.Close();
    }

    private bool DriverSteering()
    {
        InputActions.VehicleActions vehicle = playerInputScript.actions.Vehicle;
        return vehicle.enabled && (vehicle.Drive.ReadValue<Vector2>() != Vector2.zero || vehicle.Accelerate.IsPressed());
    }

    IEnumerator RenderMap(float duration)
    {
        yield return new WaitForSeconds(duration);
        // capture bird's eye view, no fog and a key colour behind for MapPaper
        bool fog = RenderSettings.fog;
        CameraClearFlags clear = mapCamera.clearFlags;
        Color background = mapCamera.backgroundColor;
        RenderSettings.fog = false;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = MapPaper.KeyColor;
        DistantPillar.Hide(true);
        mapCamera.Render();
        DistantPillar.Hide(false);
        RenderSettings.fog = fog;
        mapCamera.clearFlags = clear;
        mapCamera.backgroundColor = background;
        RenderedViewProjection = mapCamera.projectionMatrix * mapCamera.worldToCameraMatrix;

        RenderTexture.active = renderTexture;
        Texture2D screenshotTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        screenshotTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        screenshotTexture.Apply();
        RenderTexture.active = null;

        if (mapPicture != null)
        {
            Destroy(mapPicture);
        }
        mapPicture = screenshotTexture;
        mapImage[0].texture = screenshotTexture;
    }

    void Update()
    {
        float mapImageAlpha = mapImage[0].color.a;

        // flat overlay stays hidden, MapPaper shows it
        {
            elasped += Time.deltaTime;

            foreach (RawImage UIComponent in mapImage)
            {
                Color color = UIComponent.color;
                color.a = Mathf.Lerp(mapImageAlpha, 0, elasped / duration);
                UIComponent.color = color;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.GetComponent<Node>() != null)
        {
            if (other.gameObject.GetComponent<Node>().isActive)
            {
                node = other.gameObject.GetComponent<Node>();
                if (other.gameObject.GetComponent<StartEnvironmentTrace>() != null)
                {
                    startEnvironmentTraceScript = other.gameObject.GetComponent<StartEnvironmentTrace>();
                }
            }
        }
    }

}
