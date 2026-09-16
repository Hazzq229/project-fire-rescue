using UnityEngine;

public class SplitScreenManager : MonoBehaviour
{
    public Transform player1;
    public Transform player2;
    public float jarakBatasSplit = 15f; // Jarak maksimal sebelum layar terbelah

    [Header("Referensi Objek")]
    public GameObject kameraBersama; // Masukkan Virtual Camera Target Group ke sini
    public GameObject canvasSplitScreen; // Masukkan Canvas UI ke sini

    void Update()
    {
        // Menghitung jarak antar karakter
        float jarak = Vector3.Distance(player1.position, player2.position);

        if (jarak >= jarakBatasSplit)
        {
            // Jika berjauhan: Nyalakan UI Split Screen, matikan kamera bersama
            canvasSplitScreen.SetActive(true);
            kameraBersama.SetActive(false);
        }
        else
        {
            // Jika berdekatan: Matikan UI, kembali ke kamera bersama
            canvasSplitScreen.SetActive(false);
            kameraBersama.SetActive(true);
        }
    }
}