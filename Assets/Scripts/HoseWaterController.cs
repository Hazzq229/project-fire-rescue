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
        [SerializeField] private VisualEffect waterFX;
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

        private void OnDisable()
        {
            isShooting = false;
            if (waterFX) waterFX.Stop();
        }
    }
}
