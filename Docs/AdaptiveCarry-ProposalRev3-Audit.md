# Audit Adaptive Carry terhadap Proposal Rev3

Tanggal: 12 Oktober 2026. Sumber: `D:\tugas\5223600014_Andhika-Bintang-Ratnanto_Seminar-Proposal_Rev3.docx`, dibaca tanpa mengubah dokumen. Rujukan menggunakan nomor tabel/bagian dalam dokumen, bukan nomor halaman hasil perkiraan.

## Kesimpulan tahap

Demo Carry Object untuk box sudah menghubungkan bobot, target tangan, posisi Front/Back, dan gerak bersama. Tambahan permintaan pengguna berupa solo sangat lambat, ujung peti miring, dan lean menurut input yang bertentangan merupakan umpan balik gameplay yang mendukung keterbacaan usaha. Ketiganya bukan parameter keenam dalam proposal.

Keberhasilan demo ini belum berarti seluruh sistem atau validasi penelitian Rev3 selesai. Tahap berikut yang paling langsung adalah Object Type: membedakan grip Box dan Cylinder, kemudian memperluas Interaction Type untuk skenario lainnya.

Penyempurnaan lanjutan atas permintaan pengguna pada tanggal yang sama: peti langsung rata pada event grab partner, kepemilikan box solo eksklusif, aksi letakkan memakai ulang clip pickup, serta pengaktifan clip Running dan penghalusan locomotion. Ini merupakan penyempurnaan demo; belum menambah cakupan Object Type atau Interaction Type. Tidak dibuat clip animasi baru untuk letakkan maupun lari.

## Matriks cakupan

| Parameter/kebutuhan | Bukti implementasi saat ini | Status terhadap seluruh Rev3 |
| --- | --- | --- |
| Object Weight → body lean, KF-04/KF-08 | CarryAnimationLoad, kurva HeldObjectBodyLeanController, BodyLeanConstraint; beban dibagi jumlah holder. Profil kecepatan telah diuji pada 10/20/30/50 kg. | Berfungsi pada demo box; rentang penuh 1–100 kg dan referensi pose belum diverifikasi menyeluruh. |
| Object Size → hand spacing, KF-03/KF-07 | GripPointProvider dan target IK mengikuti ukuran collider; contoh box kecil/sedang/besar. | Implementasi tersedia; sweep kontinu 0,2–2 m dan seluruh kombinasi pose masih diperlukan. |
| Role → interaction pose, KF-05/KF-09 | Carry: Front/Back menurut ujung box. Hose: pegangan depan mengarahkan air, belakang mengatur tekanan dan panjang; nomor pemain tidak menentukan role. | Demo box dan hose tersedia. Survivor dan matriks variasi pose lintas kategori belum lengkap. |
| Object Type → grip style, KF-02/KF-06 | Provider grip box serta orientasi tangan tersedia. | Belum lengkap: belum ada pemetaan terpadu Box/Cylinder/Survivor/Hose sebagaimana Tabel 3.9. |
| Interaction Type → base animation, KF-01 | Pickup/Hold/Drop pada carry; dispatcher pintu/lampu; Hose Layer menggunakan pose Using_Fire_Hose dengan IK dan lean menurut tekanan. | Sebagian: Carry Object, Object Interaction, dan demo Fire Hose tersedia. Carry Survivor serta pemetaan terpadu seluruh kategori Tabel 3.9/3.19 masih diperlukan. |
| Kombinasi dinamis, KF-10 | Bobot, pembagian beban, role, target IK, dan usaha input bekerja bersama pada crate demo. | Sebagian; belum matriks lintas tipe/ukuran/bobot/role/interaksi. |

Tabel 3.6 menjadi acuan KF di atas. Script `AdaptiveAnimationManager`, `InteractionContext`, dan `ProceduralLayerProcessor` pada folder AdaptiveAnimationSystem masih berupa kerangka; perilaku demo dijalankan komponen carry, grip, dan lean yang sudah ada. Karena itu integrasi seluruh arsitektur belum dinyatakan selesai.

## Kalibrasi dibanding rancangan

Tabel 3.16 memberikan contoh bobot 10/30/50 kg → lean 5/10/15 derajat dan kecepatan 100/85/70%. Profil kecepatan demo cocok pada titik tersebut. Kurva lean scene memakai kalibrasi pengguna (10 → 3, 30 → -20, 50 → -50 derajat, batas umum 60); tanda bergantung sumbu rig. Khusus kooperatif dibatasi 25 derajat untuk menjaga jangkauan tangan. Ini berbeda secara numerik dari contoh proposal dan perlu dilaporkan sebagai kalibrasi implementasi, lalu dibandingkan dengan referensi pose, bukan dianggap cocok persis.

Contoh hand spacing pada Tabel 3.16 (ukuran 0,5/1/1,5 m → spacing 0,6/1,2/1,8 m) belum diterapkan sebagai rumus universal. Provider demo menggunakan geometri collider serta offset grip. Rentang dan kelayakan jangkauan karakter harus diuji sebelum menyatakan seluruh ukuran memenuhi rancangan.

## Sinkronisasi dan verifikasi

Pengujian Unity mencakup pickup melalui Animator event, solo tilt, kecepatan solo 0,252 unit/detik, perataan ketika partner bergabung, beban bersama 30 kg, gerak 2,55 unit/detik, input konflik tanpa translasi, lean dua arah dunia, kontak IK, tabrakan dinding, pertukaran role, pelepasan dan penonaktifan partner. Pengujian memakai scene tersimpan serta simulasi fisika dan evaluasi graph rig; ini tidak menggantikan playtest keyboard oleh pengguna.

Tabel 3.20–3.21 meminta sinkronisasi, kontak tangan, kestabilan formasi, dan transisi. Carry bersama dijalankan satu koordinator; pickup masing-masing pemain sengaja dapat dimulai terpisah sesuai permintaan pengguna. Karena itu checklist “kedua karakter memulai animasi secara bersamaan” tidak boleh dicentang untuk pickup awal ini. Hose juga memakai koordinator dua peran, dengan pengujian pickup event, IK, tekanan, panjang kabel, dan pelepasan; rincian ada pada `Cooperative-FireHose.md`. Sinkronisasi Survivor belum diuji.

Formasi saat ini hanya translasi di lantai datar, tidak berputar mengikuti arah perjalanan. Front/Back berarti ujung objek, bukan pemain yang selalu berjalan di depan. Seluruh gerak kaki khusus bukan syarat penyelesaian tahap ini karena ruang lingkup proposal mengecualikan pengembangan locomotion dasar.

## Bukti penelitian yang masih diperlukan

- Referensi animasi manual nonprocedural sebagai pembanding sebagaimana rancangan verifikasi.
- Matriks lima parameter dan pengukuran kontak/transisi pada rentang serta tipe yang ditentukan.
- Validasi responden dengan instrumen Likert Tabel 3.22–3.25, termasuk naturalness, smoothness, keterbacaan top-down, kontak, sinkronisasi, dan umpan balik kecepatan.
- Perbandingan jumlah clip/variasi terhadap pendekatan konvensional untuk reusabilitas, Tabel 3.26.

Belum ada klaim skor responden, penghematan clip terukur, atau pemenuhan 100% proposal. Perbaikan carry ini dapat menjadi fondasi pengembangan parameter berikutnya sambil daftar verifikasi di atas dilengkapi.
