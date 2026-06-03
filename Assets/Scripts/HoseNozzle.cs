using UnityEngine;
using HInteractions;
using UnityEngine.VFX;

namespace HGame.Objects
{
    public class HoseNozzle : Liftable
    {
        [Header("Hose Settings")]
        [SerializeField] private VisualEffect _waterFX; // Assign partikel air di sini
        [SerializeField] private float _shootRecoilForce = 5f;

        private bool _isShooting = false;

        protected override void Awake()
        {
            base.Awake();
            maxHolders = 1;
            if (_waterFX) _waterFX.Stop();
        }

        protected override void OnFirstPickup()
        {
            // Selang melayang (Gravity OFF) biar mudah diarahkan
            Rigidbody.useGravity = false; 
            Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            
            // Massa ringan tapi jangan 0
            Rigidbody.mass = 5f; 
            Rigidbody.drag = 3f; // Drag tinggi udara biar ga liar
            Rigidbody.angularDrag = 3f;
        }

        protected override void OnAllDropped()
        {
            base.OnAllDropped();
            SetShooting(false);
        }

        // function to be called by player controller
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
                if(state)
                {
                    _waterFX.Play();
                }
                else
                {
                    _waterFX.Stop();
                }
            }
        }

        private void FixedUpdate()
        {
            // Efek dorongan ke belakang saat nembak air
            if (_isShooting && IsLifted)
            {
                Rigidbody.AddForce(-transform.forward * _shootRecoilForce, ForceMode.Force);
            }
        }
    }
}