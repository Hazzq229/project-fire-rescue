using UnityEngine;

public class CombustObject : MonoBehaviour
{
    [SerializeField] private CombustibleConfig config;
    private CellData cellData;

    private MeshRenderer meshRenderer;
    private GameObject activeFire;
    private ParticleSystem[] fireParticles;
    private float[] startIntensities;

    public CellData Data => cellData;

    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (config != null)
        {
            cellData = new CellData(
                Vector2Int.zero,
                config.type,
                config.ignitionPoint,
                config.burningPoint,
                config.maxExtinguishPoint
            );
        }
    }

    void Update()
    {
        if (cellData.Type == CellType.NonCombustible || cellData.State == CellState.Extinguished)
            return;

        // Memetakan posisi dunia ke grid dan menyimpannya di cellData
        Vector2Int gridPos = FireManager.Instance.WorldToGrid(transform.position);
        cellData.Position = gridPos;

        if (cellData.State == CellState.Normal)
        {
            CellState floorState = FireManager.Instance.GetCellState(gridPos);
            if (floorState == CellState.Ignited || floorState == CellState.Burning)
            {
                ApplyHeat(FireManager.Instance.damagePoint * Time.deltaTime);
            }
        }
        else if (cellData.State == CellState.Ignited || cellData.State == CellState.Burning)
        {
            // Mentransfer panas ke area lantai sekitarnya
            FireManager.Instance.ApplyHeatToArea(gridPos, FireManager.Instance.damagePoint * Time.deltaTime);

            // Proses Regenerasi Api
            if (cellData.ExtinguishPoint < cellData.MaxExtinguishPoint && Time.time > cellData.LastWaterTime + config.regenDelay)
            {
                cellData.ExtinguishPoint += config.regenRate * Time.deltaTime;
                if (cellData.ExtinguishPoint > cellData.MaxExtinguishPoint)
                    cellData.ExtinguishPoint = cellData.MaxExtinguishPoint;

                UpdateFireVisual();
            }

            if (cellData.State == CellState.Ignited)
            {
                cellData.BurningPoint -= FireManager.Instance.burningRate * Time.deltaTime;
                if (cellData.BurningPoint <= 0) Burn();
            }
        }
    }

    public void ApplyHeat(float heatAmount)
    {
        if (cellData.Type == CellType.NonCombustible || cellData.State != CellState.Normal)
            return;

        cellData.IgnitionPoint -= heatAmount;
        if (cellData.IgnitionPoint <= 0) Ignite();
    }

    public void ApplyExtinguish(float amount)
    {
        if (cellData.State == CellState.Ignited || cellData.State == CellState.Burning)
        {
            cellData.ExtinguishPoint -= amount;
            cellData.LastWaterTime = Time.time;

            if (cellData.ExtinguishPoint <= 0)
            {
                cellData.State = CellState.Extinguished;
                if (activeFire != null) Destroy(activeFire);
            }
            else
            {
                UpdateFireVisual();
            }
        }
    }

    private void Ignite()
    {
        cellData.State = CellState.Ignited;
        cellData.ExtinguishPoint = cellData.MaxExtinguishPoint;

        if (config.firePrefab != null && activeFire == null)
        {
            activeFire = Instantiate(config.firePrefab, transform.position, Quaternion.identity, transform);
            fireParticles = activeFire.GetComponentsInChildren<ParticleSystem>();
            startIntensities = new float[fireParticles.Length];

            for (int i = 0; i < fireParticles.Length; i++)
            {
                startIntensities[i] = fireParticles[i].emission.rateOverTime.constant;
            }
        }
    }

    private void Burn()
    {
        cellData.State = CellState.Burning;
        if (meshRenderer != null) meshRenderer.material.color = Color.black;
    }

    private void UpdateFireVisual()
    {
        if (fireParticles == null) return;

        float intensityRatio = cellData.ExtinguishPoint / cellData.MaxExtinguishPoint;
        for (int i = 0; i < fireParticles.Length; i++)
        {
            var emission = fireParticles[i].emission;
            emission.rateOverTime = intensityRatio * startIntensities[i];
        }
    }
}