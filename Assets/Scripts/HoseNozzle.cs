using UnityEngine;
using HInteractions;
using UnityEngine.VFX;

namespace HGame.Objects
{
    public class HoseNozzle : Liftable
    {
        [Header("Hose Settings")]
        [Tooltip("Isi jika air menggunakan komponen Visual Effect / VFX Graph.")]
        [SerializeField] private VisualEffect _waterFX;
        [Tooltip("Alternatif jika air menggunakan Particle System biasa. Boleh kosong jika memakai VFX Graph.")]
        [SerializeField] private ParticleSystem _waterParticles;
        [SerializeField] private float _shootRecoilForce = 5f;

        [Header("Extinguisher Settings - belum diaktifkan untuk tes toggle")]
        [SerializeField] private bool enableFireExtinguishing = false;
        [SerializeField] private float extinguishRate = 1.0f;
        [SerializeField] private Transform raycastOrigin = null;
        [SerializeField] private GameObject steamObject = null;

        [Header("Arc Settings")]
        [SerializeField] private int segmentCount = 15; // Jumlah potongan garis (semakin panjang jarak air, perbesar nilai ini)
        [SerializeField] private float waterSpeed = 10f; // Kecepatan pancaran awal
        [SerializeField] private float timeStep = 0.1f; // Jarak waktu antar titik

        [SerializeField, NaughtyAttributes.ReadOnly] private bool _isShooting = false;
        public bool IsShooting => _isShooting;

        protected override void Awake()
        {
            base.Awake();
            maxHolders = 1;
            if (_waterFX) _waterFX.initialEventName = "OnStop";
            if (_waterParticles)
            {
                foreach (var system in _waterParticles.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main;
                    main.playOnAwake = false;
                }
            }
        }

        protected override void OnFirstPickup()
        {
            Rigidbody.useGravity = false;
            Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            Rigidbody.mass = 5f;
            Rigidbody.drag = 3f;
            Rigidbody.angularDrag = 3f;
        }

        protected override void OnAllDropped()
        {
            base.OnAllDropped();
            SetShooting(false);
        }

        public void ToggleShooting()
        {
            if (!IsLifted) return;
            SetShooting(!_isShooting);
        }

        public void SetShooting(bool state)
        {
            // Air hanya boleh hidup selama nozzle dipegang dan script aktif.
            state = state && IsLifted && isActiveAndEnabled;
            if (state && !_waterFX && !_waterParticles)
            {
                Debug.LogWarning("HoseNozzle: isi Water FX atau Water Particles pada Inspector.", this);
                state = false;
            }
            if (state == _isShooting) return;
            _isShooting = state;
            ApplyWaterVisuals(state);
            if (!state && steamObject) steamObject.SetActive(false);
        }

        private void ApplyWaterVisuals(bool state)
        {
            if (_waterFX)
            {
                if (state) _waterFX.Play();
                else _waterFX.Stop();
            }
            if (_waterParticles)
            {
                if (state) _waterParticles.Play(true);
                else _waterParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnEnable()
        {
            _isShooting = false;
            if (_waterFX)
            {
                // Reinit membersihkan simulasi; OnStop mencegah auto-start setelah reset.
                _waterFX.initialEventName = "OnStop";
                _waterFX.Reinit();
                _waterFX.Stop();
            }
            if (_waterParticles)
                _waterParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (steamObject) steamObject.SetActive(false);
        }

        private void OnDisable()
        {
            _isShooting = false;
            ApplyWaterVisuals(false);
            if (steamObject) steamObject.SetActive(false);
        }

        private void FixedUpdate()
        {
            if (_isShooting && !IsLifted) SetShooting(false);
            if (_isShooting && IsLifted)
            {
                Rigidbody.AddForce(-transform.forward * _shootRecoilForce, ForceMode.Force);
                // Logika lama dipertahankan, tetapi OFF secara default.
                if (enableFireExtinguishing) HandleFireExtinguishing();
                else if (steamObject && steamObject.activeSelf) steamObject.SetActive(false);
            }
            else if (steamObject && steamObject.activeSelf)
            {
                steamObject.SetActive(false);
            }
        }

        private void HandleFireExtinguishing()
        {
            if (raycastOrigin == null) return;

            Vector3 startPos = raycastOrigin.position;
            Vector3 currentVelocity = raycastOrigin.forward * waterSpeed;

            for (int i = 0; i < segmentCount; i++)
            {
                // Hitung posisi jatuh di titik berikutnya berdasarkan kecepatan dan gravitasi
                Vector3 nextPos = startPos + (currentVelocity * timeStep);
                currentVelocity += Physics.gravity * timeStep;

                // Visualisasi garis lengkung di scene view
                Debug.DrawLine(startPos, nextPos, Color.blue);

                // Pengecekan tabrakan antara titik saat ini dan titik berikutnya
                if (Physics.Linecast(startPos, nextPos, out RaycastHit hit))
                {
                    if (hit.collider.TryGetComponent(out Fire fire))
                    {
                        fire.TryExtinguish(extinguishRate * Time.deltaTime);

                        if (steamObject)
                        {
                            steamObject.transform.position = fire.transform.position;
                            steamObject.SetActive(fire.GetIntensity() > 0.0f);
                        }
                    }
                    // Hentikan pembuatan sisa garis jika air sudah membentur sesuatu
                    break;
                }

                // Perbarui posisi mulai untuk segmen garis selanjutnya
                startPos = nextPos;
            }
        }
    }
}