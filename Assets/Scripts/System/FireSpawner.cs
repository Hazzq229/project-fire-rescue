using UnityEngine;

public class FireSpawner : MonoBehaviour
{
    public GameObject firePrefab;
    [Tooltip("Jumlah titik api yang akan di-spawn")]
    [Min(1)] public int numberOfFires = 5;

    [Tooltip("BoxCollider yang mendefinisikan batas area dalam bangunan")]
    public BoxCollider spawnArea;

    void Start()
    {
        SpawnFire();
    }

    void SpawnFire()
    {
        if (spawnArea == null || firePrefab == null)
        {
            Debug.LogWarning("FirePrefab atau SpawnArea belum diatur di Inspector.");
            return;
        }

        Bounds bounds = spawnArea.bounds;

        for (int i = 0; i < numberOfFires; i++)
        {
            float randomX = Random.Range(bounds.min.x, bounds.max.x);
            float randomZ = Random.Range(bounds.min.z, bounds.max.z);

            // Posisi Y disamakan dengan posisi tengah BoxCollider atau sesuaikan dengan tinggi lantai
            Vector3 spawnPosition = new Vector3(randomX, bounds.center.y, randomZ);

            Instantiate(firePrefab, spawnPosition, Quaternion.identity, this.transform);
        }
    }
}