# Adaptive Carry Demo

Scene: `Assets/Scenes/AdaptiveAnimation_Demo.unity`.

## Uji langsung

1. Masuk Play Mode. Kedua karakter sudah berdiri pada ujung berlawanan peti panjang.
2. Player1: WASD untuk gerak, F untuk ambil/letakkan, tahan Left Shift untuk lari, Q untuk lempar. Player2: tombol panah, Right Ctrl untuk ambil/letakkan, tahan Right Alt untuk lari, `/` untuk lempar. Klik Game View agar menerima input. Tombol pintu dan hose tetap E/G untuk P1, Right Shift/Enter untuk P2.
3. Tekan F. Player1 mengambil posisi Back; pada event grab ujung peti langsung naik ke tangan dan ujung tanpa partner tetap rendah. Setelah pickup selesai, WASD dapat menyeret peti sangat lambat. Tekan Right Ctrl agar Player2 mengambil posisi Front.
4. Tepat pada event grab pemain kedua, peti langsung rata pada ketinggian pegangan. Formasi baru dapat berjalan setelah animasi pickup selesai; tidak ada penundaan tambahan untuk menaikkan peti.
5. Tekan W + Up Arrow bersama untuk bergerak +Z; D + Right Arrow untuk +X. Input menggunakan sumbu dunia. Satu pemain diam atau arah input saling melawan/tegak lurus membuat formasi berhenti. Tubuh pemain yang memberi input tetap condong ke arah inputnya. Coba D + Left Arrow dan W + Down Arrow; lepas input atau samakan arah agar lean usaha kembali halus ke nol.
6. Tekan tombol ambil salah satu pemain lagi untuk meletakkan bersama: kedua pemain memakai clip pickup, objek diturunkan ke permukaan di bawahnya, lalu dilepas pada event kontak. Role dan kontrol gerak normal dipulihkan. Lempar khusus objek solo; tombol lempar saat membawa peti berat juga memulai peletakan bersama.
7. Tukarkan sisi pemain untuk membuktikan bahwa nomor pemain tidak menentukan role.

HUD menampilkan role, status, serta beban per pemain. Front adalah ujung +Z lokal objek, Back adalah -Z lokal. Kedua karakter menghadap objek dan memakai pasangan target tangan masing-masing. Role tetap selama satu sesi carry, lalu dihitung ulang pada pickup berikutnya.

## Bobot dan aset

- Objek contoh: `Coop_HeavyCrate`, model dari `Assets/Prefab/Props_Box_01.prefab`.
- Prefab siap pakai: `Assets/Prefabs/Coop_HeavyCrate.prefab`.
- Bobot animasi 60 kg dibagi rata menjadi 30 kg per pemain setelah dua pemain bergabung.
- Profil `Shared_CarryWeightSpeed`: 30 kg menghasilkan 85% dari kecepatan dasar 3, yaitu 2,55 unit/detik.
- Saat sendirian, beban 60 kg menghasilkan pengali profil 70%, dikalikan `soloSpeedMultiplier` 0,12: kecepatan 0,252 unit/detik. Beban penuh juga memengaruhi body lean.
- `maximumSoloTilt` 25 derajat membatasi kemiringan; sudut aktual dihitung dari ketinggian tangan dan dimensi peti. Saat partner grab, pose rata langsung diterapkan.
- `conflictLeanDegrees` 8 mengatur usaha tambahan dalam arah dunia. `cooperativeLoadAngleLimit` 25 membatasi lean bobot khusus kooperatif agar tangan tetap terjangkau. Kurva box biasa tetap memakai kalibrasi pengguna.
- `CarryAnimationLoad.Weight Kg` adalah sumber bobot untuk postur dan kecepatan. Mass Rigidbody tetap merupakan parameter fisika tersendiri.
- Objek solo `Box_Medium (2)`, `Box_Small (2)`, dan `Box_Big (2)` memakai profil kecepatan yang sama.
- Ketiga box solo bersifat eksklusif: pemain lain tidak bisa mengambilnya selama masih dipegang, termasuk saat animasi meletakkan berlangsung. Setelah dilepas, box tersedia kembali.
- Lari solo menggunakan pengali 1,65 (4,95 unit/detik tanpa beban); penalti bobot tetap berlaku. Heavy carry tetap memakai kecepatan formasi bersama. Akselerasi/decelerasi default 14/20 unit/detik kuadrat, smoothing animasi 0,15 detik, crossfade locomotion 0,2 detik. `LocomotionRate` mengatur tempo clip Walking/Running berdasarkan kecepatan aktual; parameter `isRunning` kini terhubung.
- Parameter `stanceGap`, ukuran BoxCollider, `carryHeightAbovePlayer`, dan `gripHeight` perlu dikalibrasi jika prop/karakter diganti. Contoh ini menggunakan collider 0,44 × 0,4 × 1,4 m, gap 0,12 m, dan placeholder Player1 yang sama untuk Player2.

## Verifikasi 12 Oktober 2026

Unity 2022.3.62f2: **4/4 pengujian lulus** dalam `AdaptiveCarry.Tests`, mencakup carry, ownership, peletakan, dan locomotion. Job terakhir: `2cd8c5c0a32341bc9e4f7a7b2f69bf48`.

- Solo: pickup internal, pembacaan bobot 10/20/30/50 kg menghasilkan kecepatan yang diperintahkan 3/2,775/2,55/2,1; lepas mengembalikan pengali 100% dan parent objek.
- Kooperatif: jalur kandidat → Animator Pickup → event Pickup_Grab berhasil untuk kedua pemain; solo miring dan bergerak lambat, lalu peti rata setelah partner bergabung; IK weight kedua karakter mencapai 1. Jarak tip tangan ke target diukur kurang dari 3,5 cm.
- Lean konflik: arah dunia diuji pada dua karakter yang berlawanan orientasi; rotasi dada terukur mengikuti input, kontak tangan tetap terjaga, dan usaha kembali nol ketika input searah. Pengujian rig memakai `RigBuilder.Evaluate`, bukan evaluasi Animator terpisah dari graph rig.
- Perpindahan kooperatif diukur dengan simulasi fisika 25 langkah: sesuai 2,55 unit/detik, jarak kedua pemain tetap. Input berlawanan berhenti, sweep membatasi gerak sebelum dinding.
- Role dapat ditukar dengan menukar sisi dan urutan pickup. Pelepasan salah satu pemain, termasuk penonaktifan partner, membersihkan kedua holder, role, dan kepemilikan gerak.
- Ketiga ukuran box tidak dapat direbut, diturunkan selama clip pickup, dilepas tanpa impulse lempar, dan dapat diambil partner setelah peletakan. Running diuji dengan kecepatan terukur yang dimasukkan ke pengendali animasi selama frame Play Mode, lalu transisi kembali ke idle.
- Tombol lempar tetap menghasilkan kecepatan awal 10 unit/detik; membatalkan peletakan dengan menonaktifkan controller memulihkan fisika objek serta kunci gerak.

Jalankan kembali melalui Unity Test Runner → EditMode → `AdaptiveCarry.Tests`. Pengujian coroutine masuk Play Mode untuk memeriksa scene yang tersimpan; pengujian gerak memberi input langsung ke integrator formasi agar tidak bergantung pada fokus keyboard editor. Kelancaran menurut pemain tetap perlu dicek dengan kontrol keyboard di Game View.

## Batas tahap ini

Formasi bergerak translasi pada bidang XZ dengan orientasi yaw objek tetap; belum ada mekanisme putar formasi atau mengikuti kemiringan medan. Solo tilt dikalibrasi untuk lantai datar demo. Kedua karakter menghadap ke dalam, sehingga Front/Back merupakan posisi relatif terhadap objek, bukan peran pemimpin/pengikut. IK, postur dan kecepatan telah terhubung, sementara clip langkah maju/mundur dan tempo kaki khusus kooperatif belum dibuat. Carry Survivor, Object Interaction dan Fire Hose Interaction belum diubah.

Audit cakupan proposal terdapat di `Docs/AdaptiveCarry-ProposalRev3-Audit.md`. Bukti visual: `Docs/Images/AdaptiveCarrySoloTilt.png`, `Docs/Images/AdaptiveCarryCooperative.png`, dan `Docs/Images/AdaptiveCarryConflictingInput.png`.
