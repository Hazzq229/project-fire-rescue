# Mengganti tampilan karakter

Player mempertahankan skeleton, Animator/Controller, collider, input, anchor carry, animation events, dan constraint IK yang sudah dipasang. Model baru menyumbangkan mesh dan material, dengan tulang mesh dihubungkan kembali ke skeleton player. Komponen gameplay pada asset model baru tidak disalin.

## Pemakaian

Slot sudah dipasang pada Player1 dan Player2 di `Gameloop_Demo` dan `AdaptiveAnimation_Demo`. Tampilan awal tetap digunakan sampai Apply diklik.

1. Import model baru sebagai FBX/prefab. Export dengan nama dan susunan tulang, rest pose, orientasi, serta skala yang sama dengan rig `Assets/Arts/Character/IDLE.fbx`. Jangan aktifkan Optimize Game Objects yang menyembunyikan tulang.
2. Di Edit Mode pilih `Player1` atau `Player2`, lalu komponen **Character Model Slot**.
3. Tarik asset dari Project ke **Replacement Model**. Klik **Validate Model**, kemudian **Apply Model**.
4. Simpan scene. Untuk prefab gunakan alur save/apply prefab Unity seperti biasa. Masing-masing player bisa memakai tampilan berbeda.
5. Gunakan **Restore Original Model** untuk kembali ke tampilan awal. Apply/Restore juga mendukung Undo.

Untuk karakter di scene lain, tambahkan Character Model Slot pada root player yang sudah memiliki sistem gameplay. Komponen mencoba mengisi Model Root dan Skeleton Root otomatis. Pada karakter saat ini: Model Root = `IDLE`, Skeleton Root = `IDLE/metarig.009/Bone`.

Tidak perlu mengganti Avatar, memindahkan Animator, atau memasang ulang referensi IK. Renderer awal hanya dinonaktifkan; skeleton dan komponen tetap aktif. Penggantian tidak memerlukan skrip update per frame.

## Batas kompatibilitas

Ini penggantian tampilan untuk rig yang sama. Model dengan rig Humanoid berbeda, nama tulang berbeda, panjang tulang/rest pose berbeda, atau skala export berbeda memerlukan retargeting/kalibrasi tersendiri. Validator menolak ketidakcocokan struktur/rest transform sebelum perubahan. Mesh harus di-skin dengan benar pada rig tersebut; validator tidak dapat menjamin kualitas weight painting model yang belum tersedia.

Animasi yang menggerakkan tulang tetap bekerja. Binding animasi khusus ke nama/path renderer (misalnya animasi blendshape wajah atau material) perlu disesuaikan untuk model baru. Sistem karakter saat ini menggunakan animasi tulang.

Simpan asset mesh/material sumber di Project karena hasil menggunakan referensi asset tersebut. Hindari menghapus `CharacterAppearance`, renderer asli, atau komponen slot secara manual sebelum Restore.

## Verifikasi

Tes memakai asset placeholder `IDLE.fbx` sebagai pengganti karena model baru belum tersedia. Pemeriksaan mesh membandingkan vertex hasil skinning dalam world space; referensi tulang, Animator, apply berulang, Undo, Restore, dan penolakan asset tidak cocok ikut diperiksa. Tes Play Mode memeriksa event pickup sebenarnya dan IK tangan kedua player saat memegang hose. Keduanya lulus. Model baru tetap perlu Validate dan pemeriksaan visual setelah diimport.
