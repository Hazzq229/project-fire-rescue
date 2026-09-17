using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Fire : MonoBehaviour
{
    [SerializeField, Range(0f, 1000f)] private float ignitionRate = 20f;
    [SerializeField, Range(0f, 1000f)] private float burningRate = 30f;
    [SerializeField, Range(0f, 1f)] private float currentIntensity = 1.0f;
    public float GetIntensity() => currentIntensity;

    private float[] startIntensities = new float[0];
    float nextRegenTime = 0;
    [SerializeField] private float regenDelay = 2.5f;
    [SerializeField] private float regenRate = .1f;

    [SerializeField] private ParticleSystem[] fireParticleSystems = new ParticleSystem[0];

    private bool isLit = true;

    private void Start()
    {
        startIntensities = new float[fireParticleSystems.Length];

        for (int i = 0; i < fireParticleSystems.Length; i++)
        {
            startIntensities[i] = fireParticleSystems[i].emission.rateOverTime.constant;
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

        if (other.TryGetComponent(out CombustObject combustObj))
        {
            // Menyalurkan panas berdasarkan rate dan intensitas api saat ini
            float heatToApply = ignitionRate * currentIntensity;
            combustObj.ApplyHeat(heatToApply);
        }
    }
}