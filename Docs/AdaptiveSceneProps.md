# Carry props — Gameloop_Demo

`FR_Level_1/AdaptiveSceneProps` mengaktifkan carry adaptif pada 207 props di level dan lampu demo saat Play Mode. Objek berawalan `Props_` diproses per benda utuh; anak mesh/buku dalam satu prefab tidak mendapat kepemilikan terpisah. Model, world pose, collider detail, dan `CombustObject` dipertahankan. Root `Carry_…` yang berskala satu menyediakan Rigidbody, kepemilikan, bobot, grip tangan, dan kategori yang dapat diperiksa lewat `AdaptiveSceneProp` di Inspector saat Play Mode.

## Ukuran dan pemain

Ukuran dihitung dari sudut bounding box renderer dalam frame tegak, dengan sumbu panjang horizontal sebagai arah depan/belakang. Tinggi CapsuleCollider pemain adalah acuan: small ≤30%, medium ≤55%, selebihnya big. Bentang horizontal >65% tinggi pemain membutuhkan dua pemain; lemari/rak juga memerlukan dua pemain ketika dimensi terpanjang >50%. Kategori objek dua pemain selalu big.

Inventaris awal: **58 small, 68 medium, 21 big satu pemain, 60 big dua pemain**. Rincian dimensi dalam meter tersedia di [AdaptiveSceneProps-inventory.csv](AdaptiveSceneProps-inventory.csv). Contoh komputer 0,626 × 0,441 × 0,647 m masuk medium; meja panjang 1,206 × 0,583 × 2,942 m membutuhkan dua pemain. Dua buku dengan nama sama dapat memiliki kategori berbeda karena skala instance berbeda.

Bobot animasi awal: small 5 kg, medium 15 kg, big solo 30 kg, kooperatif 60 kg. Kecepatan menggunakan profil bersama `Shared_CarryWeightSpeed`. Pegangan ditempatkan di permukaan ujung objek dengan jarak tangan maksimum 0,38 m; lemari tinggi memakai grip lebih rendah agar bagian bawahnya tetap di atas lantai ketika dibawa. Furnitur kooperatif memakai perilaku solo lambat/miring, pengangkatan langsung saat partner bergabung, dan lean konflik input yang sudah ada.

Props mulai kinematik agar tata letak awal tetap stabil. Sesudah dilepas, fisika/gravitasi aktif untuk peletakan dan lempar. Satu prop mempunyai satu Rigidbody dan satu Liftable; objek solo tidak dapat direbut. Carry dua pemain menggunakan peran depan/belakang relatif objek.

Adaptasi mencakup furniture, buku, komputer, kardus, tanaman, lampu, kertas, dan dekorasi `Props_` di dalam level. Struktur `Mod_`/dinding/lantai, pintu interaktif, karakter, hydrant, dan api tidak dijadikan props carry. Kotak demo dan heavy crate memakai konfigurasi adaptif yang sudah ada. FireSpawner tidak diubah.

## Kontrol hose P2

`-` atas, `P` kiri, `[` bawah, `]` kanan. `N` toggle perpanjang dan `M` toggle perpendek. Enter tetap untuk menahan tekanan pada peran belakang. Diameter kabel aktif dan segmen pendukung 0,18 m.

## Validasi

`AdaptiveScenePropsTests.ScenePropsOwnershipFormationAndInput` memeriksa cakupan scene, satu Rigidbody/Liftable per prop, klasifikasi komputer, kepemilikan eksklusif, pelepasan dengan fisika, peran dua pemain pada meja, pengangkatan langsung, cleanup partner, serta binding arah P2. Regresi assembly: 9/9 lulus, job a05acfa6dc924eca905636ffbbba2e1e. Setelah kalibrasi posisi bawa dan perluasan cakupan, tes props kembali lulus pada job 0f9f1f9f090446419e7f7bfed461103c; termasuk kontak kedua tangan pada komputer dan kedua operator meja, dengan toleransi jarak tip-target 0,035 m serta binding N/M.

Dua tes hose kembali lulus pada scene final (job 5cd4418f9c7e4262bcdd714bc18877af). Screenshot validasi: [komputer](Images/AdaptiveSceneProps-computer.png) dan [Game View](Images/AdaptiveSceneProps-GameView.png).
