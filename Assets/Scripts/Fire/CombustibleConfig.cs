using UnityEngine;

[CreateAssetMenu(fileName = "NewCombustibleConfig", menuName = "Fire System/Combustible Config")]
public class CombustibleConfig : ScriptableObject
{
    public CellType type = CellType.Combustible;
    public float ignitionPoint = 10f;
    public float burningPoint = 60f;
    public float maxExtinguishPoint = 50f;
    public float regenDelay = 2.5f;
    public float regenRate = 10f;
    public GameObject firePrefab;
}