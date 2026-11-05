using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class VehicleFuelManager : MonoBehaviour
{
    public PlayerInputInitialize playerInputScript;
    [SerializeField] private Vector2 movement;
    public float maxFuel;
    public float currentFuel;
    [SerializeField] private float defaultFuelConsumptionRate;
    [SerializeField] private float increasedFuelConsumptionRate;
    public float refuelingRate;
    // a tank's about 2 days of stops and legs (17 section 7): some 14 minutes at the wheel
    private const float tankLasts = 0.6f;

    [SerializeField] private RectTransform fuelBar;
    private float rectTransformInitialHeight;

    [SerializeField] private SkinnedMeshRenderer ketBarrelMesh;
    void Start()
    {
        rectTransformInitialHeight = fuelBar.sizeDelta.y;
    }

    // Update is called once per frame
    void Update()
    {
        movement = playerInputScript.actions.Vehicle.Drive.ReadValue<Vector2>();

        //clamp fuel
        currentFuel = Mathf.Clamp(currentFuel, 0, maxFuel);

        if (Mathf.Abs(movement.y) > 0 && ketBarrelMesh.enabled)
        {
            if (playerInputScript.actions.Vehicle.Accelerate.IsPressed())
            {
                currentFuel -= increasedFuelConsumptionRate * tankLasts * Time.deltaTime;
            }
            else
            {
                currentFuel -= defaultFuelConsumptionRate * tankLasts * Time.deltaTime;
            }
        }
        //scale fuel bar directly proportionally to current fuel level
        fuelBar.sizeDelta = new Vector2(fuelBar.sizeDelta.x, rectTransformInitialHeight * (currentFuel / maxFuel));

    }
}
