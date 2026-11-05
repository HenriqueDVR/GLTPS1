using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
public class WeatherManager : MonoBehaviour
{
    [SerializeField] private ParticleSystem[] weatherParticleSystem;
    [SerializeField] private float[] particleSystemEmissionRate;
    [SerializeField] private ParticleSystem snowParticleSystem;
    [SerializeField] private Volume volume;
    private ColorAdjustments colorAdjustments;
    [SerializeField] private Color[] tintColor;
    [SerializeField] private Color[] fogColor;
    [SerializeField] private float[] fogDensity;
    [SerializeField] private Light directionalLight;
    [SerializeField] private float[] lightIntensity;
    [SerializeField] private float[] ambientIntensity;
    private float elapsed1 = 0f;
    private float elapsed2 = 0f;


    [SerializeField] private AudioSource[] audioSource;
    [SerializeField] private float[] audioVolume;

    [SerializeField] private RawImage mapTextureOverlay;
    [SerializeField] private Color[] mapTextureOverlayColor;
    public int index;
    public bool start;
    public bool stop;
    public bool isWeatherTransitioned;
    public int[] weatherChance;
    [SerializeField] private float transitionDuration;
    [SerializeField] private float repeatInvokeDuration = 60f;
    [SerializeField] private float weatherDuration = 60f;

    private WeatherData weatherData;

    private AtmosphereStack stack;

    // for UndergroundAtmosphere, which hides them and dims the light under the deck
    public ParticleSystem SnowParticles => snowParticleSystem;

    public ParticleSystem[] WeatherParticles => weatherParticleSystem;

    public Light Sun => directionalLight;


    void Start()
    {
        //initialize elasped2 to prevent lerp to default weather state when starting
        elapsed2 = transitionDuration;

        stack = GameSession.Get<AtmosphereStack>();
        stack.UseSun(directionalLight);
        stack.UseFlakes(snowParticleSystem, weatherParticleSystem);
        fogColor[0] = stack.WeatherFog;
        fogDensity[0] = stack.WeatherDensity;
        lightIntensity[0] = stack.WeatherLight;

#if DUMP_WORLDGEN
        // -coopfastweather
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopfastweather") >= 0)
        {
            repeatInvokeDuration = 5f;
            weatherDuration = 5f;
        }
#endif
        if (CoopWeather.FollowsHost)
        {
            CoopWeather.Register(this);
            return;
        }
        StartCoroutine(StartWeather());
    }

    private struct WeatherData
    {
        public ParticleSystem weatherParticleSystem;
        public float particleSystemEmissionRate;
        public Color tintColor;
        public Color fogColor;
        public float fogDensity;
        public float lightIntensity;
        public float ambientIntensity;
        public AudioSource audioSource;
        public float audioVolume;
        public Color mapTextureOverlayColor;
    }

    IEnumerator StartWeather()
    {
#if DUMP_WORLDGEN
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coopfastweather") >= 0)
        {
            for (int i = 0; i < weatherChance.Length; i++)
            {
                weatherChance[i] = 1;
            }
        }
#endif
        index = RandomWeightedGenerator.GenerateRandomIndex(weatherChance);
#if DUMP_WORLDGEN
        Debug.Log("CoopDebug weather odds " + string.Join(",", weatherChance) + " systems " + weatherParticleSystem.Length + " -> " + index);
#endif

        //stop weather
        if (index == 0)
        {
            EndWeather();

            yield return new WaitForSeconds(repeatInvokeDuration);
            StartCoroutine(StartWeather());
        }
        // start weather
        else
        {
            BeginWeather();
            yield return new WaitForSeconds(weatherDuration);

            EndWeather();
            yield return new WaitForSeconds(transitionDuration);
            StartCoroutine(StartWeather());
        }
    }

    public void ApplyHostWeather(int hostIndex, bool on)
    {
        index = hostIndex;
        if (on)
        {
            BeginWeather();
        }
        else
        {
            EndWeather();
        }
    }

    private void BeginWeather()
    {
        elapsed2 = 0f;

        weatherData.weatherParticleSystem = weatherParticleSystem[index];
        weatherData.particleSystemEmissionRate = particleSystemEmissionRate[index];
        weatherData.tintColor = tintColor[index];
        weatherData.fogColor = fogColor[index];
        weatherData.fogDensity = fogDensity[index];
        weatherData.lightIntensity = lightIntensity[index];
        weatherData.ambientIntensity = ambientIntensity[index];
        weatherData.audioSource = audioSource[index];
        weatherData.audioVolume = audioVolume[index];
        weatherData.mapTextureOverlayColor = mapTextureOverlayColor[index];

        start = true;
        stop = false;
        CoopWeather.Changed(index, true);
    }

    private void EndWeather()
    {
        elapsed1 = 0f;

        start = false;
        stop = true;
        CoopWeather.Changed(index, false);
    }

    void Update()
    {
        if (start)
        {
            SetWeather(weatherData);
        }
        else if (stop)
        {
            StopWeather(weatherData);
        }
    }

    private void SetWeather(WeatherData weatherData)
    {
        if (elapsed1 < transitionDuration)
        {
            if (elapsed1 == 0)
            {
                weatherData.audioSource.Play();
                weatherData.weatherParticleSystem.Play();
                snowParticleSystem.Stop();
            }

            elapsed1 += Time.deltaTime;

            if (volume.profile.TryGet<ColorAdjustments>(out colorAdjustments))
            {
                colorAdjustments.colorFilter.value = Color.Lerp(tintColor[0], weatherData.tintColor, elapsed1 / transitionDuration);
            }

            stack.WeatherAmbient = Mathf.Lerp(ambientIntensity[0], weatherData.ambientIntensity, elapsed1 / transitionDuration);
            stack.WeatherFog = Color.Lerp(fogColor[0], weatherData.fogColor, elapsed1 / transitionDuration);
            stack.WeatherDensity = Mathf.Lerp(fogDensity[0], weatherData.fogDensity, elapsed1 / transitionDuration);

            stack.WeatherLight = Mathf.Lerp(stack.WeatherLight, weatherData.lightIntensity, elapsed1 / transitionDuration);
            stack.WeatherAmount = Mathf.Clamp01(elapsed1 / transitionDuration);

            ParticleSystem.EmissionModule emission = weatherData.weatherParticleSystem.emission;
            emission.rateOverTime = Mathf.Lerp(0, weatherData.particleSystemEmissionRate, elapsed1 / transitionDuration);

            weatherData.audioSource.volume = Mathf.Lerp(0, weatherData.audioVolume, elapsed1 / transitionDuration);

            // lerp map overlay color
            Color color = Color.Lerp(mapTextureOverlayColor[0], weatherData.mapTextureOverlayColor, elapsed1 / transitionDuration);
            mapTextureOverlay.color = color;
        }
        else
        {
            isWeatherTransitioned = true;
        }
    }
    private void StopWeather(WeatherData weatherData)
    {
        if (elapsed2 < transitionDuration)
        {
            elapsed2 += Time.deltaTime;

            if (volume.profile.TryGet<ColorAdjustments>(out colorAdjustments))
            {
                colorAdjustments.colorFilter.value = Color.Lerp(weatherData.tintColor, tintColor[0], elapsed2 / transitionDuration);
            }

            stack.WeatherAmbient = Mathf.Lerp(weatherData.ambientIntensity, ambientIntensity[0], elapsed2 / transitionDuration);
            stack.WeatherFog = Color.Lerp(weatherData.fogColor, fogColor[0], elapsed2 / transitionDuration);
            stack.WeatherDensity = Mathf.Lerp(weatherData.fogDensity, fogDensity[0], elapsed2 / transitionDuration);

            stack.WeatherLight = Mathf.Lerp(weatherData.lightIntensity, lightIntensity[0], elapsed2 / transitionDuration);
            stack.WeatherAmount = 1f - Mathf.Clamp01(elapsed2 / transitionDuration);

            ParticleSystem.EmissionModule emission = weatherData.weatherParticleSystem.emission;
            emission.rateOverTime = Mathf.Lerp(weatherData.particleSystemEmissionRate, 0, elapsed2 / transitionDuration);

            // lerp map overlay color
            Color color = Color.Lerp(weatherData.mapTextureOverlayColor, mapTextureOverlayColor[0], elapsed2 / transitionDuration);
            mapTextureOverlay.color = color;

            weatherData.audioSource.volume = Mathf.Lerp(weatherData.audioVolume, 0, elapsed2 / transitionDuration);
            if (weatherData.audioSource.volume == 0f)
            {
                weatherData.audioSource.Stop();
                snowParticleSystem.Play();
            }
        }
        else
        {
            isWeatherTransitioned = false;
        }
    }
}
