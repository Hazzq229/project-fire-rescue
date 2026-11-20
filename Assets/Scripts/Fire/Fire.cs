using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Fire : MonoBehaviour
{
    private static readonly HashSet<Fire> enabledFires = new HashSet<Fire>();
    private const float MinimumSurvivorHeatRate = 6f;

    [SerializeField, Range(0f, 1000f)] private float ignitionRate = 20f;
    [SerializeField, Range(0f, 1f)] private float currentIntensity = 1.0f;
    public float GetIntensity() => currentIntensity;

    private float[] startIntensities = new float[0];
    float nextRegenTime = 0;
    [SerializeField] private float regenDelay = 2.5f;
    [SerializeField] private float regenRate = .1f;

    [SerializeField] private ParticleSystem[] fireParticleSystems = new ParticleSystem[0];

    private bool isLit = true;
    private Collider heatSource;
    private bool HasActiveHeat => isLit && isActiveAndEnabled && currentIntensity > 0f;

    private void Awake() => heatSource = GetComponent<Collider>();
    private void OnEnable() => enabledFires.Add(this);
    private void OnDisable() => enabledFires.Remove(this);

    public static bool TryGetClosestActive(Vector3 point, float radius, out Vector3 position)
    {
        position = default;
        float bestDistance = radius * radius;
        bool found = false;
        foreach (var fire in enabledFires)
        {
            if (!fire || !fire.HasActiveHeat) continue;
            Vector3 candidate = fire.transform.position;
            float distance = (candidate - point).sqrMagnitude;
            if (distance > bestDistance) continue;
            bestDistance = distance;
            position = candidate;
            found = true;
        }
        return found;
    }

    public static float GetSurvivorHeat(Collider survivor, float range, out float exposureRate)
    {
        Bounds bounds = survivor.bounds;
        var center = bounds.center;
        float searchRadius = range + bounds.extents.magnitude;
        float searchRadiusSquared = searchRadius * searchRadius;
        float heatRate = 0f;
        exposureRate = 0f;
        foreach (var fire in enabledFires)
        {
            if (!fire || !fire.HasActiveHeat || fire.ignitionRate <= 0f ||
                !fire.heatSource || !fire.heatSource.enabled ||
                fire.heatSource.bounds.SqrDistance(center) > searchRadiusSquared) continue;

            Vector3 nearFire = fire.heatSource.ClosestPoint(center);
            Vector3 nearSurvivor = survivor.ClosestPoint(nearFire);
            Vector3 gap = nearFire - nearSurvivor;
            float distance = gap.magnitude;
            if (distance > range) continue;
            // Start just beyond the survivor's surface so its own collider cannot block heat.
            if (distance > .01f)
            {
                Vector3 direction = gap / distance;
                if (Physics.Raycast(nearSurvivor + direction * .005f, direction,
                    distance - .01f, ~0, QueryTriggerInteraction.Ignore)) continue;
            }

            float warmth = Mathf.Clamp01(fire.currentIntensity);
            if (range > 0f) warmth *= 1f - distance / range;
            exposureRate = Mathf.Max(exposureRate, warmth);
            heatRate = Mathf.Max(heatRate, warmth * Mathf.Max(MinimumSurvivorHeatRate, fire.ignitionRate));
        }
        return heatRate;
    }

    private void Start()
    {
        startIntensities = new float[fireParticleSystems.Length];

        for (int i = 0; i < fireParticleSystems.Length; i++)
        {
            startIntensities[i] = fireParticleSystems[i].emission.rateOverTime.constant;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterFire(this);
        }
    }

    private void Update()
    {
        if (isLit && currentIntensity < 1.0f)
            Regenerate();
    }

    private void Regenerate()
    {
        if (Time.time < nextRegenTime)
            return;

        currentIntensity += regenRate * Time.deltaTime;
        ChangeIntensity();
    }

    public bool TryExtinguish(float amount)
    {
        nextRegenTime = Time.time + regenDelay;

        currentIntensity -= amount;

        ChangeIntensity();

        if (currentIntensity <= 0)
        {
            Die();
            return true;
        }

        return false;
    }

    private void Die()
    {
        isLit = false;
        enabled = false;

        if (TryGetComponent(out Collider col))
        {
            col.enabled = false;
        }
    }

    private void ChangeIntensity()
    {
        for (int i = 0; i < fireParticleSystems.Length; i++)
        {
            var emission = fireParticleSystems[i].emission;
            emission.rateOverTime = currentIntensity * startIntensities[i];
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!isLit) return;

        float heatToApply = ignitionRate * currentIntensity;
        if (other.TryGetComponent(out CombustObject combustObj))
        {
            // Menyalurkan panas berdasarkan rate dan intensitas api saat ini
            combustObj.ApplyHeat(heatToApply);
        }
    }
}
