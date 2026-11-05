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
    [SerializeField] private Camera mapCamera;
    [SerializeField] private RenderTexture renderTexture;
    [SerializeField] private RawImage[] mapImage;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip mapSFX;
    [SerializeField] private float duration = 0.2f;
    public bool isMapEnabled;
    private float elasped;

   

    void Awake()
    {
        // MapPaper draws the sheet from the plan now. it's still in the scene, enabled it'd draw the whole map every frame
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
                MapPaper.Open(this);

                audioSource.Stop();
                audioSource.PlayOneShot(mapSFX);

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
}
