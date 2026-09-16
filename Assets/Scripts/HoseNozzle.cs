using UnityEngine;
using HInteractions;
using UnityEngine.VFX;

namespace HGame.Objects
{
    public class HoseNozzle : Liftable
    {
        [Header("Hose Settings")]
        [SerializeField] private VisualEffect _waterFX;
        [SerializeField] private float _shootRecoilForce = 5f;

        [Header("Extinguisher Settings")]
        [SerializeField] private float extinguishRate = 1.0f;
        [SerializeField] private Transform raycastOrigin = null;
        [SerializeField] private GameObject steamObject = null;

        [Header("Arc Settings")]
        [SerializeField] private int segmentCount = 15; // Jumlah potongan garis (semakin panjang jarak air, perbesar nilai ini)
        [SerializeField] private float waterSpeed = 10f; // Kecepatan pancaran awal
        [SerializeField] private float timeStep = 0.1f; // Jarak waktu antar titik

        private bool _isShooting = false;

        protected override void Awake()
        {
            base.Awake();
            maxHolders = 1;
            if (_waterFX) _waterFX.Stop();
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
            _isShooting = state;
            if (_waterFX)
            {
                if (state) _waterFX.Play();
                else _waterFX.Stop();
            }
        }

        private void FixedUpdate()
        {
            if (_isShooting && IsLifted)
            {
                Rigidbody.AddForce(-transform.forward * _shootRecoilForce, ForceMode.Force);
                HandleFireExtinguishing();
            }
            else if (steamObject && steamObject.activeSelf)
            {
                steamObject.SetActive(false);
            }
        }

        // private void HandleFireExtinguishing()
        // {
        //     if (raycastOrigin == null) return;

        //     Debug.DrawRay(raycastOrigin.position, raycastOrigin.forward * 100f, Color.red);

        //     if (Physics.Raycast(raycastOrigin.position, raycastOrigin.forward, out RaycastHit hit, 100f))
        //     {
        //         if (hit.collider.TryGetComponent(out Fire fire))
        //         {
        //             fire.TryExtinguish(extinguishRate * Time.deltaTime);

        //             if (steamObject)
        //             {
        //                 steamObject.transform.position = fire.transform.position;
        //                 steamObject.SetActive(fire.GetIntensity() > 0.0f);
        //             }
        //         }
        //     }
        // }

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