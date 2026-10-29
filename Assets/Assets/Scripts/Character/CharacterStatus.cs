using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class CharacterStatus : MonoBehaviour
{
    public int index;
    public int offsetXPos;
    public List<GameObject> newFoodbar;
    public float waterLevel = 200;
    public int maxIndex = 10;

    [SerializeField] private float slowDepletionRate = 0.5f;
    // was multiplying by deltaTime twice. tuned so 60fps matches how it was
    private const float referenceFrameRate = 60f;

    public float hungerRate = 1f;
    [SerializeField] private float maxHungerValue = 100f;

    public float thirstRate = 2f;
    [SerializeField] private float maxThirstValue = 100f;
    [SerializeField] private float maxWaterLevel = 400f;
    public float waterSlowRefillRate = 0.1f;

    public int[] temperatureLossRate;

    [SerializeField] private float maxTemperatureValue = 100f;

    [SerializeField] private RectTransform waterBar;
    private float waterBarRectTransformInitialLength;

    [SerializeField] private Image chitoHungerBar;
    private float chitoHungerBarInitialFillAmount;
    [SerializeField] private Image chitoThirstBar;
    private float chitoThirstBarInitialFillAmount;
    [SerializeField] private Image chitoTemperatureBar;
    private float chitoTemperatureBarInitialFillAmount;


    [SerializeField] private Image yuuriHungerBar;
    private float yuuriHungerBarInitialFillAmount;
    [SerializeField] private Image yuuriThirstBar;
    private float yuuriThirstBarInitialFillAmount;
    [SerializeField] private Image yuuriTemperatureBar;
    private float yuuriTemperatureBarInitialFillAmount;

    public GameObject newCampfire;


    [SerializeField] private WeatherManager weatherManagerScript;

    public

    void Start()
    {
        waterBarRectTransformInitialLength = waterBar.sizeDelta.y;

        chitoHungerBarInitialFillAmount = chitoHungerBar.fillAmount;
        chitoThirstBarInitialFillAmount = chitoThirstBar.fillAmount;
        chitoTemperatureBarInitialFillAmount = chitoTemperatureBar.fillAmount;

        yuuriHungerBarInitialFillAmount = yuuriHungerBar.fillAmount;
        yuuriThirstBarInitialFillAmount = yuuriThirstBar.fillAmount;
        yuuriTemperatureBarInitialFillAmount = yuuriTemperatureBar.fillAmount;
    }

    void Update()
    {
        if (Girls.Chito == null || Girls.Yuuri == null)
        {
            return;
        }
        CharacterItemInteraction chitoItems = Girls.Chito.Items;
        CharacterItemInteraction yuuriItems = Girls.Yuuri.Items;
        UpdateRectTransformSizeY(waterBar, waterBarRectTransformInitialLength, waterLevel, maxWaterLevel);

        UpdateImageFillAmount(chitoHungerBar, chitoHungerBarInitialFillAmount, chitoItems.hungerLevel, maxHungerValue);
        UpdateImageFillAmount(chitoThirstBar, chitoThirstBarInitialFillAmount, chitoItems.thirstLevel, maxThirstValue);
        UpdateImageFillAmount(chitoTemperatureBar, chitoTemperatureBarInitialFillAmount, chitoItems.temperatureLevel, maxTemperatureValue);

        UpdateImageFillAmount(yuuriHungerBar, yuuriHungerBarInitialFillAmount, yuuriItems.hungerLevel, maxHungerValue);
        UpdateImageFillAmount(yuuriThirstBar, yuuriThirstBarInitialFillAmount, yuuriItems.thirstLevel, maxThirstValue);
        UpdateImageFillAmount(yuuriTemperatureBar, yuuriTemperatureBarInitialFillAmount, yuuriItems.temperatureLevel, maxTemperatureValue);


        float scaledHungerRate;
        float scaledThirstRate;

        // set the scaled hunger and thirst rate
        if (Girls.Chito.Input.actions.Vehicle.enabled || Girls.Yuuri.Input.actions.Vehicle.enabled)
        {
            scaledHungerRate = hungerRate * slowDepletionRate / referenceFrameRate;
            scaledThirstRate = thirstRate * slowDepletionRate / referenceFrameRate;
        }
        else
        {
            scaledHungerRate = hungerRate / referenceFrameRate;
            scaledThirstRate = thirstRate / referenceFrameRate;
        }
        waterLevel = Mathf.Clamp(waterLevel, 0, maxWaterLevel);

        // set the water refill rate when exposed to rain
        // friend gets water from the host
        if (weatherManagerScript.index == 1 && (!chitoItems.isUnderCeiling || !yuuriItems.isUnderCeiling) && (!CoopSession.InGame || CoopSession.Instance.IsHost))
        {
            waterLevel += waterSlowRefillRate * Time.deltaTime;
        }

        foreach (Girl girl in Girls.All)
        {
            Drain(girl.Items, scaledHungerRate, scaledThirstRate);
        }
    }

    private void Drain(CharacterItemInteraction items, float scaledHungerRate, float scaledThirstRate)
    {
        // colder out in the weather
        float scaledTemperatureLossRate = temperatureLossRate[items.isUnderCeiling ? 0 : weatherManagerScript.index] / referenceFrameRate;
        items.hungerLevel = Mathf.Clamp(items.hungerLevel, 0, maxHungerValue);
        items.hungerLevel -= scaledHungerRate * Time.deltaTime;
        items.thirstLevel = Mathf.Clamp(items.thirstLevel, 0, maxThirstValue);
        items.thirstLevel -= scaledThirstRate * Time.deltaTime;
        items.temperatureLevel = Mathf.Clamp(items.temperatureLevel, 0, maxTemperatureValue);
        items.temperatureLevel -= scaledTemperatureLossRate * Time.deltaTime;
    }

    private void UpdateRectTransformSizeY(RectTransform rectTransform, float initalLength, float currentLength, float maxLength)
    {
        //scale length directly proportionally to current length
        rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, initalLength * (currentLength / maxLength));
    }

    private void UpdateImageFillAmount(Image image, float initalFillAmount, float currentFillAmount, float maxFillAmount)
    {
        image.fillAmount = initalFillAmount * (currentFillAmount / maxFillAmount);
    }
}
