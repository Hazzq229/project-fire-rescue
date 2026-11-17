# Object Interaction — pintu dan lampu

Scene pengembangan: `Assets/Scenes/AdaptiveAnimation_Demo.unity`.

## Uji langsung

1. Masuk Play Mode dan klik Game View. Kosongkan tangan terlebih dahulu.
2. Lampu `Props_Lamp_Interactive` berada di kanan area peti berat, pada posisi (4, 0.5, 1). Dekati sisi saklar dari arah -Z, sekitar (4, 0.68, 0.52).
3. Player1 memakai **E**, Player2 memakai **Right Shift**. Karakter menyesuaikan posisi, memainkan base animation `DoorPush`, lalu men-toggle lampu pada event kontak. Tekan kembali setelah animasi selesai untuk mengubah keadaan lagi.
4. Lihat perubahan cahaya pada lantai serta status ON/OFF di HUD. Objek lampu tetap aktif ketika cahayanya mati, sehingga dapat diinteraksikan lagi.
5. Pintu `Demo_InteractiveDoor` berada di belakang area box, dekat (-2, 0.857, 3). Dekati titik depan/belakang untuk membuka atau menutup dengan tombol yang sama. Animasi push/pull ditentukan sisi dan keadaan pintu.

## Struktur

- `ObjectInteractionTarget`: kontrak dan daftar objek aktif. Menyediakan titik interaksi, pemilihan animasi, status, reservasi, aksi saat kontak, serta pelepasan reservasi.
- `DoorInteractable`: implementasi pintu dengan keempat titik existing dan ayunan engsel. Referensi prefab tetap dipertahankan.
- `LampInteractable`: implementasi saklar yang mengatur `Light.enabled` dan warna bulb melalui MaterialPropertyBlock. Tidak mengubah material bersama atau mematikan seluruh GameObject.
- `DoorInteractionController`: sekarang mendispatch seluruh Object Interaction. Nama kelas dan field lama dipertahankan agar referensi scene/prefab pintu tidak hilang; menu Add Component menampilkan **Object Interaction Controller**. Properti baru `IsInteractingWithObject` dan `ActiveTarget` tersedia.
- `DoorAnimationEvents.Object_Contact`: jalur kontak generik. Alias `Door_Contact`, `Door_Open`, dan `Door_Close` tetap kompatibel dengan event pada clip lama. Tidak dibuat clip baru.

Satu target direservasi satu pemain sampai animasi selesai. Target dipilih dari titik terdekat dalam jangkauan; kandidat dengan jalur alignment terhalang ditolak. Interaksi saat membawa objek, pickup, atau aksi lain ditolak. Alignment menggunakan perpindahan dan rotasi halus sebelum animasi dimulai. Timeout, target/controller nonaktif, dan penyelesaian aksi melepaskan reservasi serta kunci kontrol. Event duplikat tidak mengulang aksi.

## Prefab dan sumber

Prefab reusable: `Assets/Prefabs/Props_Lamp_Interactive.prefab`. Modelnya berasal dari nested prefab `Assets/Prefab/Props_Lamp.prefab`. Instance visual demo dibuat statis tanpa Rigidbody dinamis dan tanpa komponen pembakaran. Prefab sumber dan lampu pada level lain tidak diubah.

Setup pintu aktif yang ditemukan berada di **FR_Level_01_AnimationDemo** (lima pintu, dua controller pemain). Scene **FR_Level_01** tidak memiliki komponen DoorInteractable/DoorInteractionController pada pemeriksaan. Pintu demo menggunakan prefab `PF_InteractiveDoor` dari setup tersebut. Kedua scene level tidak disimpan ulang selama pekerjaan ini.

Pada instance pintu demo, `OpenBackPoint` digeser ke X lokal -0,12 agar capsule placeholder tidak berimpit dengan daun pintu terbuka. Pemeriksaan jalur tetap aktif; posisi yang terhalang tidak dipaksa dengan teleport menembus collider. Kalibrasi titik pada prefab/level asal tetap dipertahankan.

## Verifikasi dan batas tahap

Hasil 12 Oktober 2026: **6/6 pengujian lulus** pada Unity 2022.3.62f2, termasuk 4 regresi carry/placement/running dan 2 pengujian Object Interaction. Job: `5875563e687b4f20807d167ff60096d5`. Alignment lampu dari posisi bergeser 0,2 m juga mencapai titik interaksi dengan galat kurang dari 2,5 cm.

`ObjectInteractionTests` memeriksa toggle melalui Animator event asli, larangan di luar jangkauan/saat membawa barang, reservasi antarpemain, event duplikat, pemulihan kontrol, pembatalan sebelum kontak, dan pintu buka/tutup dari dua sisi. Bukti gambar tersimpan pada `Docs/Images/ObjectInteractionLampOn.png` dan `Docs/Images/ObjectInteractionLampOff.png`.

Ini adalah implementasi awal kategori **Interaction Type: Object Interaction**. Lampu memakai ulang base animation push sebagai gerak menjangkau saklar. Adaptasi IK otomatis untuk bermacam tinggi/jarak saklar, variasi ukuran objek, dan matriks parameter proposal belum diklaim selesai; titik demo dikalibrasi untuk placeholder saat ini.
