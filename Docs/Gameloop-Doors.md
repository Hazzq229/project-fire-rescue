# Pintu Gameloop_Demo

Scene memakai sistem `DoorInteractable` / `DoorInteractionController` dan event kontak animasi yang sudah ada.

- Lima `PF_InteractiveDoor` memiliki titik interaksi pada tinggi root pemain (world Y 0.39). Titik untuk menutup pintu mengikuti hinge melalui child baru `Gameloop_Door*_OpenFront/OpenBack`.
- Titik OpenBack pintu toilet `PF_InteractiveDoor (1)` digeser menjauhi dinding agar alignment pemain tidak terhalang collider.
- `MainDoor_Left` dan `MainDoor_Right` memakai daun kaca asli, masing-masing dengan BoxCollider solid, Rigidbody kinematic, dan empat titik interaksi. Sudut buka -90 / +90 derajat mengarahkan kedua daun ke dalam ruangan.
- Posisi awal `Hydrant/Hose/HoseNozzle` berada di (8, 0.55, -21.8), di samping akses masuk agar sambungan nozzle tidak menutupi posisi interaksi pintu.

Kontrol: Player 1 **E**, Player 2 **Right Shift**; dekati sisi pintu dengan tangan kosong.

Regresi: `GameloopDoorTests.AllDoorsOpenCloseFromBothSidesAtPlayerHeight` menjalankan controller dan kontak animasi sebenarnya untuk tujuh pintu: buka/tutup dari depan dan belakang (28 aksi). Tes juga memeriksa collider solid terhadap capsule sweep pemain, tinggi marker, marker terbuka mengikuti hinge, dan pelepasan kontrol setelah animasi.

Hasil terakhir: **Passed**, 1/1 tes, 28 aksi selesai; job Unity MCP `c3cffc3e98a54215bc38f61f69eab779`. Console setelah tes tidak berisi error atau warning.
