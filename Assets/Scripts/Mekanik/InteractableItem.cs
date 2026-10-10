using System.Collections.Generic;
using UnityEngine;

namespace DisasterSquad.Mekanik
{
    [RequireComponent(typeof(Rigidbody))]
    public class InteractableItem : MonoBehaviour
    {
        public enum HolderCapacity
        {
            OnePlayer = 1,
            TwoPlayers = 2
        }

        [Header("Holder Rules")]
        [SerializeField] private HolderCapacity holderCapacity = HolderCapacity.OnePlayer;
        [SerializeField, Range(1, 2)] private int requiredHoldersToMove = 1;

        [Header("Carry Feel")]
        [SerializeField, Range(0f, 1f)] private float singleHolderSpeedMultiplier = 0.75f;
        [SerializeField, Range(0f, 1f)] private float fullCoopSpeedMultiplier = 0.95f;
        [SerializeField, Range(0f, 1f)] private float waitingForPartnerSpeedMultiplier = 0.25f;
        [SerializeField] private bool forceHolderFaceItem = true;

        [Header("Grab Point")]
        [SerializeField] private Transform grabPoint;
        [SerializeField] private Vector3 heldRotationOffset;

        [Header("Physics While Held")]
        [SerializeField] private bool disableGravityWhileHeld = false;
        [SerializeField] private float heldDrag = 3f;
        [SerializeField] private float heldAngularDrag = 3f;

        private readonly List<PlayerInteractionManager> holders = new();
        private readonly List<LayerRecord> defaultLayers = new();

        private Rigidbody itemRigidbody;
        private bool defaultUseGravity;
        private float defaultDrag;
        private float defaultAngularDrag;
        private RigidbodyInterpolation defaultInterpolation;

        public Rigidbody Rigidbody => itemRigidbody;
        public IReadOnlyList<PlayerInteractionManager> Holders => holders;
        public int HolderCount => holders.Count;
        public int MaxHolders => (int)holderCapacity;
        public int RequiredHoldersToMove => Mathf.Clamp(requiredHoldersToMove, 1, MaxHolders);
        public bool HasEnoughHoldersToMove => HolderCount >= RequiredHoldersToMove;
        public bool IsHeld => HolderCount > 0;
        public bool ForceHolderFaceItem => forceHolderFaceItem;
        public Vector3 HeldRotationOffset => heldRotationOffset;

        private void Awake()
        {
            itemRigidbody = GetComponent<Rigidbody>();
            defaultUseGravity = itemRigidbody.useGravity;
            defaultDrag = itemRigidbody.drag;
            defaultAngularDrag = itemRigidbody.angularDrag;
            defaultInterpolation = itemRigidbody.interpolation;
        }

        private void OnValidate()
        {
            requiredHoldersToMove = Mathf.Clamp(requiredHoldersToMove, 1, (int)holderCapacity);
        }

        public bool CanBeHeldBy(PlayerInteractionManager holder)
        {
            return holder != null && !holders.Contains(holder) && HolderCount < MaxHolders;
        }

        public bool TryAddHolder(PlayerInteractionManager holder, int heldLayer)
        {
            if (!CanBeHeldBy(holder)) return false;

            holders.Add(holder);

            if (HolderCount == 1)
            {
                SaveAndChangeLayers(heldLayer);
                ApplyHeldPhysics();
            }

            return true;
        }

        public void RemoveHolder(PlayerInteractionManager holder)
        {
            if (!holders.Remove(holder)) return;

            if (HolderCount == 0)
            {
                RevertLayers();
                RestorePhysics();
            }
        }

        public float GetCurrentSpeedMultiplier()
        {
            if (!HasEnoughHoldersToMove)
                return waitingForPartnerSpeedMultiplier;

            if (MaxHolders > 1 && HolderCount >= 2)
                return fullCoopSpeedMultiplier;

            return singleHolderSpeedMultiplier;
        }

        public Vector3 GetGrabPosition(Vector3 handPosition)
        {
            if (grabPoint != null)
                return grabPoint.position;

            Collider itemCollider = GetComponentInChildren<Collider>();
            if (itemCollider != null)
                return itemCollider.ClosestPoint(handPosition);

            return transform.position;
        }

        private void ApplyHeldPhysics()
        {
            itemRigidbody.useGravity = !disableGravityWhileHeld && defaultUseGravity;
            itemRigidbody.drag = heldDrag;
            itemRigidbody.angularDrag = heldAngularDrag;
            itemRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void RestorePhysics()
        {
            itemRigidbody.useGravity = defaultUseGravity;
            itemRigidbody.drag = defaultDrag;
            itemRigidbody.angularDrag = defaultAngularDrag;
            itemRigidbody.interpolation = defaultInterpolation;
        }

        private void SaveAndChangeLayers(int heldLayer)
        {
            if (heldLayer < 0) return;

            defaultLayers.Clear();
            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (Collider itemCollider in colliders)
            {
                defaultLayers.Add(new LayerRecord(itemCollider.gameObject, itemCollider.gameObject.layer));
                itemCollider.gameObject.layer = heldLayer;
            }
        }

        private void RevertLayers()
        {
            foreach (LayerRecord layerRecord in defaultLayers)
                layerRecord.GameObject.layer = layerRecord.Layer;

            defaultLayers.Clear();
        }

        private readonly struct LayerRecord
        {
            public LayerRecord(GameObject gameObject, int layer)
            {
                GameObject = gameObject;
                Layer = layer;
            }

            public GameObject GameObject { get; }
            public int Layer { get; }
        }
    }
}
