using UnityEngine;

public enum CellType { Combustible, NonCombustible }
public enum CellState { Normal, Ignited, Burning, Extinguished }

[System.Serializable]
public class CellData
{
    public Vector2Int Position;
    public CellType Type;
    public CellState State;
    public float IgnitionPoint;
    public float BurningPoint;
    public float ExtinguishPoint;
    public float MaxExtinguishPoint;
    public float LastWaterTime;

    public CellData(Vector2Int pos, CellType type, float ignition, float burning, float maxExtinguish = 100f)
    {
        Position = pos;
        Type = type;
        State = CellState.Normal;
        IgnitionPoint = ignition;
        BurningPoint = burning;
        MaxExtinguishPoint = maxExtinguish;
        ExtinguishPoint = maxExtinguish;
        LastWaterTime = 0f;
    }
}