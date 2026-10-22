using UnityEngine;

public class FloorZoneModifier : MonoBehaviour
{
    [Header("Cell Configuration Override")]
    public CellType cellType = CellType.Combustible;
    public float ignitionPoint = 30f;
    public float burningPoint = 60f;

    [Tooltip("Jika di-uncheck, hanya cellType yang diubah, ignition & burning point tetap default FireManager.")]
    public bool overridePoints = true;

    private void OnDrawGizmos()
    {
        Gizmos.color = (cellType == CellType.NonCombustible)
            ? new Color(0.2f, 0.2f, 0.2f, 0.4f)
            : new Color(1f, 0.5f, 0f, 0.4f);

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}