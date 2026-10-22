using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class FireTester : MonoBehaviour
{
    [Header("Target Location")]
    [Tooltip("Tarik GameObject ini di Scene View untuk memilih sel grid yang ingin diuji.")]
    [SerializeField] private Transform targetPoint;

    [Header("Gizmo Highlight Settings")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.5f, 0f, 0.6f);

    public void IgniteSelectedCell()
    {
        if (FireManager.Instance == null)
        {
            Debug.LogWarning("[FireTester] FireManager instance tidak ditemukan!");
            return;
        }

        if (targetPoint == null)
        {
            Debug.LogWarning("[FireTester] Target Point belum di-assign!");
            return;
        }

        Vector2Int gridPos = FireManager.Instance.WorldToGrid(targetPoint.position);

        if (FireManager.Instance.IsValidPosition(gridPos))
        {
            FireManager.Instance.TriggerOnCellIgnited(gridPos);
            Debug.Log($"[FireTester] Menyalakan api di sel grid: {gridPos}");
        }
        else
        {
            Debug.LogWarning($"[FireTester] Posisi {gridPos} berada di luar area grid!");
        }
    }

    private void OnDrawGizmos()
    {
        if (targetPoint == null || FireManager.Instance == null) return;

        Vector2Int gridPos = FireManager.Instance.WorldToGrid(targetPoint.position);

        if (FireManager.Instance.IsValidPosition(gridPos))
        {
            float cellSize = FireManager.Instance.cellSize;
            Vector3 gridOrigin = FireManager.Instance.GridOrigin;

            // Hitung posisi tengah sel grid berdasarkan koordinat targetPoint
            Vector3 center = new Vector3(
                gridOrigin.x + (gridPos.x * cellSize) + (cellSize / 2f),
                gridOrigin.y + 0.08f,
                gridOrigin.z + (gridPos.y * cellSize) + (cellSize / 2f)
            );

            // Visualisasi sel yang sedang ditunjuk
            Gizmos.color = highlightColor;
            Gizmos.DrawCube(center, new Vector3(cellSize, 0.05f, cellSize));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(center, new Vector3(cellSize, 0.05f, cellSize));
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(FireTester))]
public class FireTesterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FireTester tester = (FireTester)target;

        EditorGUILayout.Space(10);
        if (GUILayout.Button("Ignite Selected Cell", GUILayout.Height(35)))
        {
            if (Application.isPlaying)
            {
                tester.IgniteSelectedCell();
            }
            else
            {
                Debug.LogWarning("[FireTester] Tombol ini hanya berfungsi saat Play Mode berjalan.");
            }
        }
    }
}
#endif