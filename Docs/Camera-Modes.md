# Pilihan & Kontrol Kamera Gameloop_Demo

Pada `Main Camera`, komponen **Camera Mode Options** mengelola mode kamera permainan:

## 1. Mode Kamera yang Tersedia
- **SharedCurrent (Kamera Zoom Bersama)**:
  - Satu kamera Cinemachine mengikuti Target Group kedua player.
  - Saat kedua player saling menjauh, kamera otomatis zoom-out membingkai kedua pemain tanpa membelah layar (asli demo).
  - Kamera split, VCam P1/P2, dan Canvas Split Screen dinonaktifkan untuk efisiensi render GPU.
- **AdaptiveSplitScreen (Kamera Split Adaptif)**:
  - Saat kedua player berdekatan (jarak < 15 unit): kamera tetap 1 (kamera bersama).
  - Saat kedua player berjauhan (jarak >= 15 unit): layar terbelah secara otomatis menjadi split-screen (kiri Player 1, kanan Player 2).
  - Dilengkapi ambang **histeresis** (default 1.5 unit): kamera baru menyatu kembali saat jarak < 13.5 unit untuk mencegah flickering visual di perbatasan jarak.

---

## 2. Cara Menggunakan Toggle
Tersedia 3 cara mudah untuk memilih atau beralih mode kapan saja:

1. **Tombol Keyboard di Runtime**:
   - Tekan tombol **`F1`** saat gameplay berjalan untuk langsung beralih antara mode **Zoom** dan mode **Split Screen**.
   - Shortcut key dapat dikonfigurasi pada field `Toggle Key` di komponen `Camera Mode Options`.
2. **On-Screen Toggle Button**:
   - Terdapat tombol/badge di pojok kiri atas layar permainan yang menampilkan status mode aktif saat ini dan dapat diklik langsung dengan mouse untuk berganti mode.
   - Opsi ini bisa dihidup-matikan lewat toggle `Show On Screen Toggle` di Inspector.
3. **Inspector Unity**:
   - Ubah dropdown **Mode** pada komponen `Camera Mode Options` di Inspector, baik sebelum play maupun saat Play Mode berlangsung.

---

## 3. Catatan Teknis & Integritas Sistem
- Kamera P1/P2 me-render masing-masing player ke RenderTexture `LayarP1` dan `LayarP2` yang ditampilkan di `Canvas Split Screen`.
- Sudut kamera bersama memakai offset Transposer `(0, 6, -7)`. Sudut kamera terpisah memakai pitch 38°, offset follow `(0, 4, -5)`, dan FOV 50°.
- Seluruh input menggunakan **Unity New Input System** (`UnityEngine.InputSystem.Keyboard`).
- **Tidak ada script gameplay, animasi, atau interaksi lain yang diubah.**
