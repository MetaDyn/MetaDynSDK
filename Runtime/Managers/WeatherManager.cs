using UnityEngine;
using UnityEngine.UI;

#if METADYN_AZURE_SKY
using UnityEngine.AzureSky;
#endif

namespace MetaDyn
{
    public class WeatherManager : MonoBehaviour
    {
#if METADYN_AZURE_SKY
        [Header("Azure Sky Reference")]
        [SerializeField] private AzureCoreSystem azureCoreSystem;
#endif

        [Header("Weather Settings")]
        [SerializeField] private int dayWeatherIndex = 0;
        [SerializeField] private int nightWeatherIndex = 1;

        [Header("Fog Settings")]
        [SerializeField] private bool controlFog = true;
        [SerializeField] private FogMode fogMode = FogMode.Exponential;

        [Header("Day Fog Settings")]
        [SerializeField] private bool dayFogEnabled = false;
        [SerializeField] private Color dayFogColor = new Color(0.8f, 0.9f, 1f, 1f);
        [SerializeField] private float dayFogDensity = 0.01f;
        [SerializeField] private float dayFogStart = 10f;
        [SerializeField] private float dayFogEnd = 100f;

        [Header("Night Fog Settings")]
        [SerializeField] private bool nightFogEnabled = true;
        [SerializeField] private Color nightFogColor = new Color(0.1f, 0.1f, 0.2f, 1f);
        [SerializeField] private float nightFogDensity = 0.03f;
        [SerializeField] private float nightFogStart = 5f;
        [SerializeField] private float nightFogEnd = 80f;

        [Header("UI Settings")]
        [SerializeField] private Button weatherToggleButton;
        [SerializeField] private Image weatherIcon;
        [SerializeField] private Sprite dayIcon;
        [SerializeField] private Sprite nightIcon;

        [Header("Current State")]
        [SerializeField] private bool isNight = false;

        private void Start()
        {
            if (weatherToggleButton != null)
            {
                weatherToggleButton.onClick.AddListener(ToggleWeather);
            }
            SetWeatherToDay();
        }

        public void ToggleWeather()
        {
            if (isNight) SetWeatherToDay();
            else SetWeatherToNight();
        }

        public void SetWeatherToDay()
        {
#if METADYN_AZURE_SKY
            if (azureCoreSystem != null)
                azureCoreSystem.weatherSystem.SetGlobalWeather(dayWeatherIndex);
#endif
            isNight = false;
            if (controlFog) ApplyFogSettings(dayFogEnabled, dayFogColor, dayFogDensity, dayFogStart, dayFogEnd);
            if (weatherIcon != null && dayIcon != null) weatherIcon.sprite = dayIcon;
        }

        public void SetWeatherToNight()
        {
#if METADYN_AZURE_SKY
            if (azureCoreSystem != null)
                azureCoreSystem.weatherSystem.SetGlobalWeather(nightWeatherIndex);
#endif
            isNight = true;
            if (controlFog) ApplyFogSettings(nightFogEnabled, nightFogColor, nightFogDensity, nightFogStart, nightFogEnd);
            if (weatherIcon != null && nightIcon != null) weatherIcon.sprite = nightIcon;
        }

        private void ApplyFogSettings(bool enabled, Color color, float density, float start, float end)
        {
            RenderSettings.fog = enabled;
            if (enabled)
            {
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = color;
                if (fogMode == FogMode.Linear)
                {
                    RenderSettings.fogStartDistance = start;
                    RenderSettings.fogEndDistance = end;
                }
                else RenderSettings.fogDensity = density;
            }
        }

        private void OnDestroy()
        {
            if (weatherToggleButton != null) weatherToggleButton.onClick.RemoveListener(ToggleWeather);
        }
    }
}