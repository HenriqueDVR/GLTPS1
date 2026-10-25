using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class UISettings : MonoBehaviour
{
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private float defaultXAxisMaxSpeed = 1f;
    [SerializeField] private float defaultYAxisMaxSpeed = 0.01f;
    public float mouseSensitivtyXAxisSpeed = 0.15f;
    public float mouseSensitivtyYAxisSpeed = 0.0015f;
    [SerializeField] private Slider ADSSensitivitySlider;
    public float ADSSensitivtyXAxisSpeed = 0.105f;
    public float ADSSensitivtyYAxisSpeed = 0.00105f;
    [SerializeField] private Slider globalAudioVolumeSlider;

    void Awake()
    {
        FrameLimit.Apply();
        AddFrameLimitButton();

        //set initial slider values
        mouseSensitivitySlider.value = PlayerPrefs.GetFloat("mouseSensitivitySliderValue", mouseSensitivitySlider.value);
        ADSSensitivitySlider.value = PlayerPrefs.GetFloat("ADSSensitivitySliderValue", ADSSensitivitySlider.value);
        globalAudioVolumeSlider.value = PlayerPrefs.GetFloat("globalAudioVolumeSlider", globalAudioVolumeSlider.value);

        SetSettings();
    }

    void Update()
    {
        SetSettings();
    }

    public void SetSettings()
    {
        //set mouse sensitivity
        mouseSensitivtyXAxisSpeed = mouseSensitivitySlider.value * defaultXAxisMaxSpeed;
        mouseSensitivtyYAxisSpeed = mouseSensitivitySlider.value * defaultYAxisMaxSpeed;

        //set ADS sensitivity
        ADSSensitivtyXAxisSpeed = ADSSensitivitySlider.value * defaultXAxisMaxSpeed;
        ADSSensitivtyYAxisSpeed = ADSSensitivitySlider.value * defaultYAxisMaxSpeed;

        //set master volume
        AudioListener.volume = globalAudioVolumeSlider.value;
    }
    // copy of the return button, in the gap between the ADS and volume sliders.
    // the game scene has a second UISettings off the page, so go by the sliders' parent, and only once
    private void AddFrameLimitButton()
    {
        Transform page = ADSSensitivitySlider.transform.parent;
        Transform template = page.Find("Return");
        if (template == null || page.Find("Frame Limit") != null)
        {
            return;
        }
        GameObject copy = Instantiate(template.gameObject, page, false);
        copy.name = "Frame Limit";
        RectTransform rect = (RectTransform)copy.transform;
        RectTransform ads = (RectTransform)ADSSensitivitySlider.transform;
        RectTransform volume = (RectTransform)globalAudioVolumeSlider.transform;
        rect.anchorMin = ads.anchorMin;
        rect.anchorMax = ads.anchorMax;
        rect.pivot = ads.pivot;
        rect.anchoredPosition = (ads.anchoredPosition + volume.anchoredPosition) / 2f;
        rect.sizeDelta = new Vector2(Mathf.Max(ads.sizeDelta.x, 420f), rect.sizeDelta.y);
        TMPro.TextMeshProUGUI label = copy.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        label.enableWordWrapping = false;
        label.text = FrameLimit.Label;
        Button button = copy.GetComponent<Button>();
        // scene listeners can't be removed, so replace
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() =>
        {
            FrameLimit.Next();
            label.text = FrameLimit.Label;
        });
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("mouseSensitivitySliderValue", mouseSensitivitySlider.value);
        PlayerPrefs.SetFloat("ADSSensitivitySliderValue", ADSSensitivitySlider.value);
        PlayerPrefs.SetFloat("globalAudioVolumeSlider", globalAudioVolumeSlider.value);
        PlayerPrefs.Save();
    }
}
