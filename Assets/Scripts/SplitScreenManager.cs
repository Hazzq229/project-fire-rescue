using UnityEngine;
using Cinemachine;

/// <summary>
/// Mengelola transisi split screen adaptif berdasarkan jarak antara player 1 dan player 2.
/// Ketika kedua player berdekatan, kamera bersama aktif.
/// Ketika kedua player berjauhan melebihi jarakBatasSplit, kamera terpisah (split screen) aktif.
/// Dilengkapi ambang histeresis untuk mencegah flickering di perbatasan jarak.
/// </summary>
public class SplitScreenManager : MonoBehaviour
{
    [Header("Player Targets")]
    public Transform player1;
    public Transform player2;

    [Header("Distance Settings")]
    [Tooltip("Jarak minimal kedua player sebelum kamera membelah (split).")]
    public float jarakBatasSplit = 15f;

    [Tooltip("Margin histeresis agar kamera tidak berkedip saat player bergerak di sekitar batas jarak.")]
    [Range(0.5f, 5f)]
    public float histeresis = 1.5f;

    [Header("Shared Camera & UI")]
    public GameObject kameraBersama;       // Virtual Camera Target Group (Shared)
    public GameObject canvasSplitScreen;   // Canvas UI yang menampung RawImage split

    [Header("Cinemachine Target Group")]
    [SerializeField] private CinemachineTargetGroup targetGroup;

    [SerializeField, Min(0f)]
    private float playerTargetWeight = 1f;

    [SerializeField, Min(0f)]
    private float playerTargetRadius = 1f;

    [Header("Split Cameras & VCams (Opsional)")]
    public GameObject player1Camera;
    public GameObject player2Camera;
    public GameObject player1VirtualCamera;
    public GameObject player2VirtualCamera;

    public bool IsSplitActive { get; private set; }

    private void Awake()
    {
        ResolvePlayerReferences();
    }

    private void OnEnable()
    {
        EvaluateDistance(true);
    }

    private void OnDisable()
    {
        // Saat komponen dinonaktifkan (misalnya saat mode Shared Zoom Camera),
        // pastikan tampilan kembali ke kamera bersama dan split dimatikan.
        ApplyState(false);
    }

    private void Update()
    {
        EvaluateDistance(false);
    }

    /// <summary>
    /// Dipanggil oleh GameplayPlayerSpawner setelah player runtime selesai dibuat.
    /// Jangan mengandalkan GameObject.Find("Player1/Player2") karena prefab hasil spawn
    /// biasanya bernama Player(Clone).
    /// </summary>
    public void SetPlayerTargets(Transform p1, Transform p2)
    {
        // Hapus target pemain sebelumnya jika masih terdaftar.
        RemoveTargetIfPresent(player1);
        RemoveTargetIfPresent(player2);

        // Perbarui referensi pemain runtime.
        player1 = p1;
        player2 = p2;

        // Masukkan instance pemain yang baru ke Target Group.
        AddTargetIfMissing(player1);
        AddTargetIfMissing(player2);

        if (targetGroup != null)
            targetGroup.DoUpdate();

        // Evaluasi ulang apakah shared camera atau split-screen aktif.
        ForceEvaluate();

        Debug.Log(
            $"[SplitScreenManager] Target Group updated. " +
            $"P1={player1?.name}, P2={player2?.name}",
            this
        );
    }

    private void RemoveTargetIfPresent(Transform target)
    {
        if (targetGroup == null || target == null)
            return;

        if (targetGroup.FindMember(target) >= 0)
            targetGroup.RemoveMember(target);
    }

    private void AddTargetIfMissing(Transform target)
    {
        if (targetGroup == null || target == null)
            return;

        if (targetGroup.FindMember(target) < 0)
        {
            targetGroup.AddMember(
                target,
                playerTargetWeight,
                playerTargetRadius
            );
        }
    }

    public void ForceEvaluate()
    {
        EvaluateDistance(true);
    }

    private void EvaluateDistance(bool force)
    {
        if (player1 == null || player2 == null)
        {
            ResolvePlayerReferences();
            if (player1 == null || player2 == null) return;
        }

        float jarak = Vector3.Distance(player1.position, player2.position);

        if (!IsSplitActive)
        {
            // Belum split: membelah jika melebihi batas split
            if (jarak >= jarakBatasSplit || (force && jarak >= jarakBatasSplit))
            {
                ApplyState(true);
            }
        }
        else
        {
            // Sedang split: menyatu kembali jika jarak kurang dari (batas split - histeresis)
            float mergeThreshold = Mathf.Max(1f, jarakBatasSplit - histeresis);
            if (jarak < mergeThreshold)
            {
                ApplyState(false);
            }
        }
    }

    public void ApplyState(bool split)
    {
        IsSplitActive = split;

        if (canvasSplitScreen != null && canvasSplitScreen.activeSelf != split)
        {
            canvasSplitScreen.SetActive(split);
        }

        if (kameraBersama != null && kameraBersama.activeSelf == split)
        {
            kameraBersama.SetActive(!split);
        }

        if (player1Camera != null && player1Camera.activeSelf != split)
        {
            player1Camera.SetActive(split);
        }

        if (player2Camera != null && player2Camera.activeSelf != split)
        {
            player2Camera.SetActive(split);
        }

        if (player1VirtualCamera != null && player1VirtualCamera.activeSelf != split)
        {
            player1VirtualCamera.SetActive(split);
        }

        if (player2VirtualCamera != null && player2VirtualCamera.activeSelf != split)
        {
            player2VirtualCamera.SetActive(split);
        }
    }

    private void ResolvePlayerReferences()
    {
        if (player1 == null)
        {
            var p1Obj = GameObject.Find("Player1");
            if (p1Obj != null) player1 = p1Obj.transform;
        }

        if (player2 == null)
        {
            var p2Obj = GameObject.Find("Player2");
            if (p2Obj != null) player2 = p2Obj.transform;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (player1 != null && player2 != null)
        {
            Gizmos.color = IsSplitActive ? Color.red : Color.green;
            Gizmos.DrawLine(player1.position, player2.position);
        }
    }
}