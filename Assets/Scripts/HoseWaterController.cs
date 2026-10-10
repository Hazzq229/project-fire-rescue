using HInteractions;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.VFX;

namespace HGame.Objects
{
    // Attach beside PhysicCableCon on the held endpoint, not on the hose root.
    [DisallowMultipleComponent]
    public class HoseWaterController : MonoBehaviour
    {
        [Header("Water Visual")]
        [SerializeField] private VisualEffect waterFX;

        [Header("Fire Extinguishing")]
        [SerializeField] private bool enableFireExtinguishing = false;
        [SerializeField] private float extinguishRate = 1.0f;
        [SerializeField] private Transform raycastOrigin;
        // [SerializeField] private GameObject steamObject;

        [Header("Water Arc")]
        [SerializeField] private int segmentCount = 15;
        [SerializeField] private float waterSpeed = 10f;
        [SerializeField] private float timeStep = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;
        [SerializeField, ReadOnly] private bool isShooting;

        private Liftable liftable;
        public bool IsShooting => isShooting;

        private void Awake()
        {
            liftable = GetComponent<Liftable>();
            if (!liftable)
                Debug.LogError("HoseWaterController harus satu GameObject dengan PhysicCableCon/Liftable.", this);

            if (waterFX) waterFX.initialEventName = "OnStop";
        }

        private void OnEnable()
        {
            isShooting = false;
            if (!waterFX) return;
            waterFX.initialEventName = "OnStop";
            waterFX.Reinit();
            waterFX.Stop();
        }

        private void LateUpdate()
        {
            // Stop after the last player drops this endpoint.
            if (isShooting && (!liftable || !liftable.isActiveAndEnabled ||
                !liftable.IsLifted || !waterFX || !waterFX.isActiveAndEnabled))
                SetShooting(false);
        }

        public void ToggleShooting()
        {
            SetShooting(!isShooting);
        }

        public void SetShooting(bool value)
        {
            if (value)
            {
                if (!isActiveAndEnabled || !liftable ||
                    !liftable.isActiveAndEnabled || !liftable.IsLifted)
                {
                    if (debugLogs)
                        Debug.LogWarning("[Hose] Ujung selang belum dipegang atau komponennya tidak aktif.", this);
                    return;
                }

                if (!waterFX || !waterFX.isActiveAndEnabled)
                {
                    Debug.LogWarning("[Hose] Isi Water FX dan aktifkan GameObject serta komponen Visual Effect-nya.", this);
                    return;
                }
            }

            if (isShooting == value) return;
            isShooting = value;
            enableFireExtinguishing = value;
            if (waterFX)
            {
                if (value)
                {
                    waterFX.pause = false;
                    waterFX.Play();
                }
                else waterFX.Stop();
            }

            if (debugLogs)
                Debug.Log(value ? "[Hose] Air ON" : "[Hose] Air OFF", this);
        }

        private void FixedUpdate()
        {
            if (!liftable || !liftable.isActiveAndEnabled ||
                !liftable.IsLifted)
            {
                SetShooting(false);
                return;
            }

            if (enableFireExtinguishing)
            {
                HandleFireExtinguishing();
            }
        }

        private void HandleFireExtinguishing()
        {
            if (!raycastOrigin)
                return;

            Vector3 startPos = raycastOrigin.position;
            Vector3 currentVelocity = raycastOrigin.forward * waterSpeed;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 nextPos =
                    startPos +
                    (currentVelocity * timeStep);

                currentVelocity += Physics.gravity * timeStep;

                // Visualisasi trajectory di Scene View.
                Debug.DrawLine(
                    startPos,
                    nextPos,
                    Color.blue
                );

                if (Physics.Linecast(
                    startPos,
                    nextPos,
                    out RaycastHit hit))
                {
                    if (hit.collider.TryGetComponent(out Fire fire))
                        fire.TryExtinguish(extinguishRate * Time.fixedDeltaTime);

                    // Air berhenti ketika mengenai objek pertama.
                    //break;
                }

                startPos = nextPos;
            }
        }
        private void OnDisable()
        {
            isShooting = false;
            if (waterFX) waterFX.Stop();
        }

        private void OnDrawGizmosSelected()
        {
            if (!raycastOrigin)
                return;

            Vector3 startPos = raycastOrigin.position;
            Vector3 currentVelocity = raycastOrigin.forward * waterSpeed;

            Gizmos.color = Color.blue;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 nextPos =
                    startPos +
                    currentVelocity * timeStep;

                currentVelocity += Physics.gravity * timeStep;

                Gizmos.DrawLine(startPos, nextPos);
                Gizmos.DrawSphere(nextPos, 0.025f);

                startPos = nextPos;
            }
        }
    }
}
