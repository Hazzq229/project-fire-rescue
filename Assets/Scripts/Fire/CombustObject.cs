using UnityEngine;

public class CombustObject : MonoBehaviour
{
    public enum ObjectType
    {
        Combustible,
        Noncombustible
    }
    public enum ObjectState
    {
        Normal,
        Ignited,
        Burning,
        Extinguised
    }

    [SerializeField] ObjectType type = ObjectType.Combustible;
    [SerializeField] ObjectState state = ObjectState.Normal;
    [SerializeField, Range(0f, 100f)] float ignitionPoint = 10f;
    [SerializeField, Range(0f, 100f)] float burningPoint = 60f;

    [Header("References")]
    [SerializeField] private GameObject firePrefab; // Prefab api yang akan di-spawn

    private float currentIgnition;
    private float currentBurning;
    private MeshRenderer meshRenderer;

    void Start()
    {
        currentIgnition = ignitionPoint;
        currentBurning = burningPoint;
        meshRenderer = GetComponent<MeshRenderer>();
    }

    public void ApplyHeat(float heatAmount)
    {
        if (type == ObjectType.Noncombustible || state == ObjectState.Extinguised || state == ObjectState.Burning)
            return;

        if (state == ObjectState.Normal)
        {
            currentIgnition -= heatAmount * Time.deltaTime;

            if (currentIgnition <= 0)
            {
                Ignite();
            }
        }
        else if (state == ObjectState.Ignited)
        {
            currentBurning -= heatAmount * Time.deltaTime;

            if (currentBurning <= 0)
            {
                Burn();
            }
        }
    }

    private void Ignite()
    {
        state = ObjectState.Ignited;

        // Memunculkan api di titik origin object
        if (firePrefab != null)
        {
            Instantiate(firePrefab, transform.position, Quaternion.identity, transform);
        }

        // TODO: Implementasi perubahan rupa saat Ignited
        // Contoh: Mengganti material emissive, menambahkan efek partikel asap,
        // atau mengubah warna awal seperti berikut:
        // if (meshRenderer != null) meshRenderer.material.color = new Color(1f, 0.5f, 0.5f);
    }

    private void Burn()
    {
        state = ObjectState.Burning;

        // Mengubah rupa menjadi hangus
        if (meshRenderer != null)
        {
            meshRenderer.material.color = Color.black;
        }
    }
}