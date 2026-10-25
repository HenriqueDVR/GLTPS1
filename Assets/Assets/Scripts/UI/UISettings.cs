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
    // option buttons are copies of the return button. fps limit goes in the gap between the
    // ADS and volume sliders, display ones under the volume slider.
    // the game scene has a second UISettings off the page, so go by the sliders' parent, and only once
    private void AddFrameLimitButton()
    {
        Transform page = ADSSensitivitySlider.transform.parent;
        if (page.Find("Return") == null || page.Find("Frame Limit") != null)
        {
            return;
        }
        Vector2 ads = ((RectTransform)ADSSensitivitySlider.transform).anchoredPosition;
        Vector2 volume = ((RectTransform)globalAudioVolumeSlider.transform).anchoredPosition;
        Vector2 step = new Vector2(0f, (ads.y - volume.y) / 2f);
        List<System.Action> refresh = new List<System.Action>();
        AddOption(page, "Frame Limit", (ads + volume) / 2f, () => FrameLimit.Label, FrameLimit.Next, refresh);
        AddOption(page, "Display Mode", volume - step, () => DisplaySettings.ModeLabel, DisplaySettings.NextMode, refresh);
        AddOption(page, "Resolution", volume - step * 2f, () => DisplaySettings.SizeLabel, DisplaySettings.NextSize, refresh);
        AddOption(page, "Render Resolution", volume - step * 3f, () => DisplaySettings.RenderLabel, DisplaySettings.NextRenderHeight, refresh);
    }

    private void AddOption(Transform page, string name, Vector2 position, System.Func<string> label, System.Action next, List<System.Action> refresh)
    {
        GameObject copy = Instantiate(page.Find("Return").gameObject, page, false);
        copy.name = name;
        RectTransform rect = (RectTransform)copy.transform;
        RectTransform ads = (RectTransform)ADSSensitivitySlider.transform;
        rect.anchorMin = ads.anchorMin;
        rect.anchorMax = ads.anchorMax;
        rect.pivot = ads.pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(Mathf.Max(ads.sizeDelta.x, 420f), rect.sizeDelta.y);
        TMPro.TextMeshProUGUI text = copy.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        text.enableWordWrapping = false;
        text.text = label();
        refresh.Add(() => text.text = label());
        Button button = copy.GetComponent<Button>();
        // scene listeners can't be removed, so replace
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() =>
        {
            next();
            // a mode change can change the resolution too
            foreach (System.Action update in refresh)
            {
                update();
            }
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
