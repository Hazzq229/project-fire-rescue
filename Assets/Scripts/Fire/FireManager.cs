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
                floorGrid[x, y] = new CellData(new Vector2Int(x, y), CellType.Combustible, IgnitionPoint, burningPoint);
            }
        }
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

            // Proses Regenerasi Api
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
                    if (targetCell.Type == CellType.Combustible && targetCell.State == CellState.Normal && !newlyIgnited.Contains(targetCell.Position))
                    {
                        targetCell.IgnitionPoint -= damage;
                        if (targetCell.IgnitionPoint <= 0) newlyIgnited.Add(targetCell.Position);
                    }
                }
            }
        }
    }

    public Vector3 GetCellWorldPosition(Vector2Int gridPos)
    {
        return new Vector3(
            GridOrigin.x + (gridPos.x * cellSize) + (cellSize / 2f),
            GridOrigin.y + 0.5f, // Ketinggian ditarik sedikit ke atas agar sejajar dengan collider tembok
            GridOrigin.z + (gridPos.y * cellSize) + (cellSize / 2f)
        );
    }

    public void TriggerOnCellIgnited(Vector2Int pos)
    {
        if (activeFireCells.Count >= spreadLimit) return;
        CellData cell = floorGrid[pos.x, pos.y];
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
                Vector3 spawnPos = new Vector3(GridOrigin.x + (cell.Position.x * cellSize) + (cellSize / 2), GridOrigin.y, GridOrigin.z + (cell.Position.y * cellSize) + (cellSize / 2));
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
                // Warna default saat Edit Mode
                Gizmos.color = Color.green;

                // Jika sedang Play Mode, sesuaikan warna dengan kondisi sel
                if (Application.isPlaying && floorGrid != null)
                {
                    CellData cell = floorGrid[x, y];
                    Gizmos.color = (cell.State == CellState.Ignited || cell.State == CellState.Burning)
                        ? Color.red
                        : Color.green;
                }

                // Offset Y sebesar 0.05f agar garis tidak tertimbun mesh lantai
                Vector3 center = new Vector3(
                    GridOrigin.x + (x * cellSize) + (cellSize / 2f),
                    GridOrigin.y + 0.05f,
                    GridOrigin.z + (y * cellSize) + (cellSize / 2f)
                );

                Gizmos.DrawWireCube(center, new Vector3(cellSize, 0f, cellSize));
            }
        }
    }
}