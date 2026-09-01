using EcsFramework;
using UnityEngine;

namespace EcsFramework
{

    public class DayNightCycleSystem : ISystem
    {
        public DayNightConfig config;
        public float currentHour = 6f;

        private Material skyboxMaterial;

        public void OnCreate(World world)
        {
            config = Resources.Load<DayNightConfig>("Config/DayNightConfig");
        }

        public void OnUpdate(World world, float dt)
        {
            if (config == null) return;

            currentHour += Time.deltaTime * config.timeMultiplier;
            if (currentHour >= config.hourOfDay) currentHour = 0f;
            DayNight.DayNightClock.Hour = currentHour;

            float weight = DayNightWeight();

            var lights = world.Query<DayNightComponent>();
            int lightCount = 0;
            foreach (var e in lights)
            {
                var c = e.Get<DayNightComponent>();
                if (c.light == null) continue;
                lightCount++;

                if (c.isSun)
                {
                    float angle = SunElevation(currentHour);
                    c.ApplyDirection(new Vector3(angle, 0f, 0f));
                    c.ApplyColorTemperature(config.sunTemperatureCurve.Evaluate(weight));
                    c.ApplyIntensity(config.sunIntensityCurve.Evaluate(weight));
                }
                else
                {
                    c.ApplyColorTemperature(config.moonTemperatureCurve.Evaluate(weight));
                    c.ApplyIntensity(config.moonIntensityCurve.Evaluate(weight));
                }
            }

            UpdateSkybox();
        }

        private float SunElevation(float hour)
        {
            float t = hour / config.hourOfDay;
            return Mathf.Sin(t * Mathf.PI * 2f - Mathf.PI / 2f) * 90f;
        }

        private float DayNightWeight() => currentHour / config.hourOfDay;

        private void UpdateSkybox()
        {
            if (skyboxMaterial == null)
            {
                skyboxMaterial = Resources.Load<Material>("SkyBox/DayNightMat");
                RenderSettings.skybox = skyboxMaterial;
            }
            if (skyboxMaterial == null) return;

            float weight = DayNightWeight();
            skyboxMaterial.SetFloat("_Rotation", 360f * weight);
            skyboxMaterial.SetFloat("_Blend", config.skyboxBlendCurve.Evaluate(weight));
        }
    }
}