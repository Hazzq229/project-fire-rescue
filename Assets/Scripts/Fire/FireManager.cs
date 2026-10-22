using System.Collections.Generic;
using UnityEngine;

public class FireManager : MonoBehaviour
{
    public static FireManager Instance { get; private set; }

    [Header("Grid Settings")]
    public int gridSizeX = 50;
    public int gridSizeY = 50;
    public float cellSize = 1f;
    public float IgnitionPoint = 50f;
    public float burningPoint = 100f;
    public Vector3 GridOrigin => transform.position;

    [Header("Zone Detection")]
    [Tooltip("Layer khusus untuk GameObject FloorZoneModifier")]
    [SerializeField] private LayerMask zoneLayerMask;

    [Header("Fire Parameters")]
    public float damagePoint = 5f;
    public float burningRate = 2f;
    public int spreadLimit = 100;
    public float tickRate = 0.5f;

    [Header("Visual & Regen")]
    public GameObject firePrefab;
    public float regenDelay = 2.5f;
    public float regenRate = 10f;

    [Header("Wall Occlusion")]
    [SerializeField] private LayerMask wallLayerMask;

    private CellData[,] floorGrid;
    private HashSet<Vector2Int> activeFireCells = new HashSet<Vector2Int>();
    private float nextTickTime;

    private Dictionary<Vector2Int, GameObject> activeFireObjects = new Dictionary<Vector2Int, GameObject>();
    private Dictionary<Vector2Int, ParticleSystem[]> activeFireParticles = new Dictionary<Vector2Int, ParticleSystem[]>();
    private Dictionary<Vector2Int, float[]> particleStartIntensities = new Dictionary<Vector2Int, float[]>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        InitializeGrid();
    }

    private void InitializeGrid()
    {
        floorGrid = new CellData[gridSizeX, gridSizeY];

        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                Vector3 cellWorldCenter = GetCellWorldPosition(pos);

                CellType type = CellType.Combustible;
                float ignition = IgnitionPoint;
                float burning = burningPoint;

                // Deteksi apakah ada FloorZoneModifier di koordinat sel ini
                Collider[] zoneColliders = Physics.OverlapBox(
                    cellWorldCenter,
                    new Vector3(cellSize * 0.45f, 0.5f, cellSize * 0.45f),
                    Quaternion.identity,
                    zoneLayerMask
                );

                foreach (Collider col in zoneColliders)
                {
                    if (col.TryGetComponent(out FloorZoneModifier modifier))
                    {
                        type = modifier.cellType;
                        if (modifier.overridePoints)
                        {
                            ignition = modifier.ignitionPoint;
                            burning = modifier.burningPoint;
                        }
                        break;
                    }
                }

                floorGrid[x, y] = new CellData(pos, type, ignition, burning);
            }
        }
    }

    public Vector3 GetCellWorldPosition(Vector2Int gridPos)
    {
        return new Vector3(
            GridOrigin.x + (gridPos.x * cellSize) + (cellSize / 2f),
            GridOrigin.y + 0.05f,
            GridOrigin.z + (gridPos.y * cellSize) + (cellSize / 2f)
        );
    }

    private void Update()
    {
        if (Time.time >= nextTickTime)
        {
            ProcessFireTick();
            nextTickTime = Time.time + tickRate;
        }
    }

    private void ProcessFireTick()
    {
        if (activeFireCells.Count == 0) return;

        List<Vector2Int> newlyIgnited = new List<Vector2Int>();
        List<Vector2Int> currentFires = new List<Vector2Int>(activeFireCells);

        foreach (Vector2Int pos in currentFires)
        {
            CellData cell = floorGrid[pos.x, pos.y];

            if (cell.ExtinguishPoint < cell.MaxExtinguishPoint && Time.time > cell.LastWaterTime + regenDelay)
            {
                cell.ExtinguishPoint += regenRate * tickRate;
                if (cell.ExtinguishPoint > cell.MaxExtinguishPoint) cell.ExtinguishPoint = cell.MaxExtinguishPoint;
                UpdateCellVisual(cell);
            }

            if (cell.State == CellState.Ignited)
            {
                cell.BurningPoint -= burningRate;
                if (cell.BurningPoint <= 0)
                {
                    cell.State = CellState.Burning;
                }
            }

            if (activeFireCells.Count + newlyIgnited.Count < spreadLimit)
            {
                ApplyMooreNeighborhoodDamage(pos, newlyIgnited, damagePoint);
            }
        }

        foreach (Vector2Int newPos in newlyIgnited) TriggerOnCellIgnited(newPos);
    }

    private void ApplyMooreNeighborhoodDamage(Vector2Int pos, List<Vector2Int> newlyIgnited, float damage)
    {
        Vector3 originPos = GetCellWorldPosition(pos);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                if (activeFireCells.Count + newlyIgnited.Count >= spreadLimit) return;

                Vector2Int neighborPos = new Vector2Int(pos.x + x, pos.y + y);

                if (IsValidPosition(neighborPos))
                {
                    Vector3 targetPos = GetCellWorldPosition(neighborPos);

                    // Cek apakah ada tembok yang menghalangi lintasan api ke sel tetangga
                    if (Physics.Linecast(originPos, targetPos, wallLayerMask))
                    {
                        continue; // Api terhalang tembok, lewati sel ini
                    }

                    CellData targetCell = floorGrid[neighborPos.x, neighborPos.y];

                    // Hanya merambat ke sel yang berkategori Combustible
                    if (targetCell.Type == CellType.Combustible && targetCell.State == CellState.Normal && !newlyIgnited.Contains(targetCell.Position))
                    {
                        targetCell.IgnitionPoint -= damage;
                        if (targetCell.IgnitionPoint <= 0) newlyIgnited.Add(targetCell.Position);
                    }
                }
            }
        }
    }

    public void TriggerOnCellIgnited(Vector2Int pos)
    {
        if (activeFireCells.Count >= spreadLimit) return;
        CellData cell = floorGrid[pos.x, pos.y];

        // Mencegah sel NonCombustible dinyalakan
        if (cell.Type == CellType.NonCombustible) return;

        cell.State = CellState.Ignited;
        cell.ExtinguishPoint = cell.MaxExtinguishPoint;
        activeFireCells.Add(pos);
        UpdateCellVisual(cell);
    }

    public void TriggerOnCellSpread(Vector2Int pos, bool isFloor)
    {
        TriggerOnCellIgnited(pos);
    }

    public void ApplyExtinguish(Vector2Int pos, float amount)
    {
        if (!IsValidPosition(pos)) return;
        CellData cell = floorGrid[pos.x, pos.y];

        if (cell.State == CellState.Ignited || cell.State == CellState.Burning)
        {
            cell.ExtinguishPoint -= amount;
            cell.LastWaterTime = Time.time;

            if (cell.ExtinguishPoint <= 0)
            {
                cell.State = CellState.Extinguished;
                activeFireCells.Remove(pos);
            }
            UpdateCellVisual(cell);
        }
    }

    public void TriggerOnCellExtinguished(Vector2Int pos)
    {
        CellData cell = floorGrid[pos.x, pos.y];
        cell.State = CellState.Extinguished;
        activeFireCells.Remove(pos);
        UpdateCellVisual(cell);
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt((worldPos.x - GridOrigin.x) / cellSize);
        int y = Mathf.FloorToInt((worldPos.z - GridOrigin.z) / cellSize);
        return new Vector2Int(x, y);
    }

    public bool IsValidPosition(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < gridSizeX && pos.y >= 0 && pos.y < gridSizeY;
    }

    public CellState GetCellState(Vector2Int pos)
    {
        return IsValidPosition(pos) ? floorGrid[pos.x, pos.y].State : CellState.Normal;
    }

    private void UpdateCellVisual(CellData cell)
    {
        if (cell.State == CellState.Ignited || cell.State == CellState.Burning)
        {
            if (!activeFireObjects.ContainsKey(cell.Position))
            {
                Vector3 spawnPos = GetCellWorldPosition(cell.Position);
                GameObject fire = Instantiate(firePrefab, spawnPos, Quaternion.identity);
                activeFireObjects[cell.Position] = fire;

                ParticleSystem[] pSystems = fire.GetComponentsInChildren<ParticleSystem>();
                activeFireParticles[cell.Position] = pSystems;

                float[] startIntensities = new float[pSystems.Length];
                for (int i = 0; i < pSystems.Length; i++) startIntensities[i] = pSystems[i].emission.rateOverTime.constant;
                particleStartIntensities[cell.Position] = startIntensities;
            }

            float intensityRatio = cell.ExtinguishPoint / cell.MaxExtinguishPoint;
            ParticleSystem[] particles = activeFireParticles[cell.Position];
            float[] starts = particleStartIntensities[cell.Position];

            for (int i = 0; i < particles.Length; i++)
            {
                var emission = particles[i].emission;
                emission.rateOverTime = intensityRatio * starts[i];
            }
        }
        else if (cell.State == CellState.Extinguished)
        {
            if (activeFireObjects.TryGetValue(cell.Position, out GameObject fire))
            {
                Destroy(fire);
                activeFireObjects.Remove(cell.Position);
                activeFireParticles.Remove(cell.Position);
                particleStartIntensities.Remove(cell.Position);
            }
        }
    }

    public void ApplyHeatToArea(Vector2Int centerPos, float damage)
    {
        if (activeFireCells.Count >= spreadLimit) return;
        List<Vector2Int> newlyIgnited = new List<Vector2Int>();
        ApplyMooreNeighborhoodDamage(centerPos, newlyIgnited, damage);
        foreach (Vector2Int pos in newlyIgnited) TriggerOnCellIgnited(pos);
    }

    private void OnDrawGizmos()
    {
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                Vector3 center = GetCellWorldPosition(new Vector2Int(x, y));

                // Warna default
                Gizmos.color = Color.green;

                if (Application.isPlaying && floorGrid != null)
                {
                    CellData cell = floorGrid[x, y];

                    if (cell.Type == CellType.NonCombustible)
                    {
                        Gizmos.color = Color.gray; // Sel tahan api
                    }
                    else if (cell.State == CellState.Ignited || cell.State == CellState.Burning)
                    {
                        Gizmos.color = Color.red; // Sel terbakar
                    }
                    else
                    {
                        Gizmos.color = Color.green;
                    }
                }

                Gizmos.DrawWireCube(center, new Vector3(cellSize, 0f, cellSize));
            }
        }
    }
}