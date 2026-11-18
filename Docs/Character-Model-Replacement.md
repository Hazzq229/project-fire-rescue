# Mengganti tampilan karakter

Player mempertahankan skeleton gameplay, Animator/Controller, collider, input, anchor carry, animation events, dan constraint IK yang sudah dipasang. Model pengganti memakai skeleton dan bind pose miliknya sendiri. CharacterModelAnimatorMirror menyinkronkan rotasi dan menyelesaikan kontak tangan pada proporsi model pengganti. Pada konfigurasi Gameloop saat ini, kepala/wajah/topi memakai mesh persisten pada root rigid yang mengikuti pose kepala tanpa mewarisi shear armature.

## Pemakaian

`Gameloop_Demo` membuat player dari `Assets/Prefabs/Player.prefab`; edit prefab tersebut di Prefab Mode untuk mengubah tampilan kedua player yang di-spawn. `AdaptiveAnimation_Demo` mempunyai player tersimpan di scene dan tidak dimigrasikan ulang oleh pekerjaan ini.

1. Import model baru sebagai FBX/prefab. Export dengan nama dan susunan tulang, rest pose, orientasi, serta skala yang sama dengan rig `Assets/Arts/Character/IDLE.fbx`. Jangan aktifkan Optimize Game Objects yang menyembunyikan tulang.
2. Di Edit Mode buka Player.prefab dan pilih root player, lalu komponen **Character Model Slot**. Untuk player yang memang disimpan di scene, pilih instance tersebut.
3. Tarik asset dari Project ke **Replacement Model**. Klik **Validate Model**, kemudian **Apply Model**.
4. Simpan scene. Untuk prefab gunakan alur save/apply prefab Unity seperti biasa. Masing-masing player bisa memakai tampilan berbeda.
5. Gunakan **Restore Original Model** untuk kembali ke tampilan awal. Apply/Restore juga mendukung Undo.

Untuk karakter di scene lain, tambahkan Character Model Slot pada root player yang sudah memiliki sistem gameplay. Komponen mencoba mengisi Model Root dan Skeleton Root otomatis. Pada karakter saat ini: Model Root = `IDLE`, Skeleton Root = `IDLE/metarig.009/Bone`.

Tidak perlu mengganti Avatar, memindahkan Animator, atau memasang ulang referensi IK. Renderer awal hanya dinonaktifkan; skeleton dan komponen tetap aktif. Mirror dan follower yang sudah ada bekerja saat runtime; proses pemulihan konfigurasi kepala dilakukan oleh editor.

## Apply ulang kepala dan topi — 15 November 2026

Apply menyimpan konfigurasi rigid head pada CharacterModelSlot sebelum membangun ulang CharacterAppearance. Data mencakup model sumber dan dependency hash, path tulang kepala/renderer, sembilan referensi mesh persisten, bounds renderer dalam ruang root bone, serta transform tampilan yang telah dikalibrasi. Tidak ada angka posisi/skala karakter yang disalin menjadi konstanta kode.

Ketika model yang sama dipasang ulang, Apply membangun kembali root/follower dan binding mesh kepala/topi dari data tersebut. Transform tampilan dipertahankan agar ukuran dan posisi tubuh tidak berubah. Topi sudah termasuk fit pada mesh tersimpan, sehingga CharacterHeadgearFit sementara tidak dipasang lagi. Jalur ini tidak membaca vertex atau membangun mesh saat runtime.

Restore mempertahankan data authoring kepala untuk Apply berikutnya, termasuk setelah prefab disimpan dan dibuka ulang. Undo mengambil snapshot lengkap slot sebelum hierarchy lama dihancurkan. Cleanup mesh sementara tidak boleh menghapus asset persisten yang dipakai bersama.

Konfigurasi tersimpan berlaku untuk model dan kalibrasi yang sama. Jika FBX atau dependency-nya berubah, atau Headgear Scale/Lift/Name diganti, Apply menolak sebelum menghapus tampilan yang sedang terpasang. Mesh kepala harus dikalibrasi/dibangun ulang untuk perubahan tersebut; tugas ini tidak menambahkan generator kalibrasi untuk model arbitrer. Data tersimpan tidak dipaksakan pada asset model lain; jalur penggantian generik tetap tersedia. Pemeriksaan dependency hash bersifat konservatif dan dapat mendeteksi perubahan material/dependency walaupun vertex belum berubah.

## Batas kompatibilitas

Ini penggantian tampilan untuk keluarga rig yang kompatibel. Model dengan rig Humanoid berbeda, nama tulang berbeda, panjang tulang/rest pose berbeda, atau skala export berbeda memerlukan retargeting/kalibrasi tersendiri. Validator memeriksa asset, root skeleton dan renderer; untuk rigid head tersimpan, path, vertex count, bindpose dan dependency model juga diperiksa. Validator generik tidak membuktikan semua rest transform atau kualitas weight painting.

Animasi yang menggerakkan tulang tetap bekerja. Binding animasi khusus ke nama/path renderer (misalnya animasi blendshape wajah atau material) perlu disesuaikan untuk model baru. Sistem karakter saat ini menggunakan animasi tulang.

Simpan asset mesh/material sumber di Project karena hasil menggunakan referensi asset tersebut. Hindari menghapus `CharacterAppearance`, renderer asli, atau komponen slot secara manual sebelum Restore.

## Verifikasi

Pengujian generik awal tetap mencakup geometri, Animator, Undo/Restore dan penolakan model tidak cocok. Pengujian Gameloop terbaru memakai Rubah_P1 yang sudah dikalibrasi pada Player.prefab. ApplyModelHeadTests membandingkan seluruh vertex baked sembilan bagian kepala/wajah/topi sebelum Apply pertama, setelah Apply berulang, Undo, Restore dan prefab save/reload. Referensi asset mesh, root-space culling bounds, transform tampilan, rig/controller, perlindungan shared mesh dan penolakan kalibrasi berbeda ikut diperiksa. Hasil Play Mode setelah pemasangan ulang dicatat di tasks/lessons.md.

Run akhir `9fef5cf9465e4ee5baf4cd13164dcbf8` lulus 7/7: dua tes Editor di atas dan lima tes yang masuk Play Mode untuk kestabilan kepala kedua player saat berjalan/berbelok, gerak/dash independen, pickup hose melalui event asli, kontak/clearance hose dan kontak/body lean furnitur. Player.prefab sudah melalui dua Apply dan disimpan sebelum run ini. Pergeseran bentuk kepala maksimum sekitar .0000102 m; kontak furnitur di bawah .00036 m dan lean komputer sekitar 24 derajat. Console akhir tanpa error/warning. Ini pengujian terarah di Editor, bukan validasi standalone build atau seluruh suite project.

Scene Gameloop_Demo dan AdaptiveAnimation_Demo, input, serta asset mesh kepala persisten tidak berubah. Player.prefab berubah untuk menyimpan visual yang dibangun ulang dan data konfigurasi kepala. Editor dikembalikan ke Gameloop_Demo di luar Play Mode tanpa perubahan scene yang belum disimpan.
