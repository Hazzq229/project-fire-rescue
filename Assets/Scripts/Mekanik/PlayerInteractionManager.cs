using System.Collections.Generic;
using UnityEngine;

namespace DisasterSquad.Mekanik
{
    [RequireComponent(typeof(PlayerManager))]
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerInteractionManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerManager playerManager;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private Transform handTransform;

        [Header("Detection")]
        [SerializeField] private LayerMask interactableMask = ~0;
        [SerializeField] private int heldObjectLayer = 6;

        [Header("Joint Settings")]
        [SerializeField] private float jointSpring = 1500f;
        [SerializeField] private float jointDamper = 100f;
        [SerializeField] private float rotateForce = 200f;

        [Header("Drop Settings")]
        [SerializeField] private bool throwOnDrop = false;
        [SerializeField] private float throwForce = 8f;

        [Header("Optional Legacy Input")]
        [SerializeField] private bool useLegacyInput;
        [SerializeField] private KeyCode legacyInteractKey = KeyCode.E;

        private readonly List<InteractableItem> candidates = new();

        private Rigidbody playerRigidbody;
        private SpringJoint grabJoint;
        private InteractableItem currentCandidate;

        public InteractableItem HeldItem { get; private set; }
        public InteractableItem CurrentCandidate => currentCandidate;
        public bool HasHeldItem => HeldItem != null;

        private void Awake()
        {
            if (playerManager == null)
                playerManager = GetComponent<PlayerManager>();

            if (inputReader == null)
                inputReader = GetComponent<PlayerInputReader>();

            if (handTransform == null)
                handTransform = transform;

            playerRigidbody = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (inputReader != null)
                inputReader.OnInteractPressed += HandleInteractPressed;
        }

        private void OnDisable()
        {
            if (inputReader != null)
                inputReader.OnInteractPressed -= HandleInteractPressed;
        }

        private void HandleInteractPressed()
        {
            TryInteract();
        }

        private void Update()
        {
            if (useLegacyInput && Input.GetKeyDown(legacyInteractKey))
                TryInteract();
        }

        private void FixedUpdate()
        {
            if (HeldItem != null)
                RotateHeldItem();
        }

        private void OnTriggerEnter(Collider other)
        {
            InteractableItem item = FindInteractableItem(other);
            if (item == null || candidates.Contains(item)) return;

            candidates.Add(item);
            RefreshCurrentCandidate();
        }

        private void OnTriggerExit(Collider other)
        {
            InteractableItem item = FindInteractableItem(other);
            if (item == null) return;

            candidates.Remove(item);
            RefreshCurrentCandidate();
        }

        public bool TryInteract()
        {
            PulseInteractState();

            if (HeldItem != null)
            {
                DropHeldItem();
                return true;
            }

            return TryPickUp(currentCandidate);
        }

        public bool TryPickUp(InteractableItem item)
        {
            if (item == null || !item.CanBeHeldBy(this)) return false;

            if (!item.TryAddHolder(this, heldObjectLayer)) return false;

            HeldItem = item;
            CreateGrabJoint(item);
            ApplyCarryModifiers(item);
            RefreshHolderModifiers(item);
            RefreshCurrentCandidate();

            return true;
        }

        public void DropHeldItem()
        {
            if (HeldItem == null) return;

            InteractableItem droppedItem = HeldItem;
            DestroyGrabJoint();

            HeldItem = null;
            droppedItem.RemoveHolder(this);

            if (throwOnDrop && droppedItem.HolderCount == 0)
                ThrowItem(droppedItem);

            playerManager.SetCarriedItem(null);
            playerManager.ClearCarryModifiers();

            RefreshHolderModifiers(droppedItem);
            RefreshCurrentCandidate();
        }

        public void SetWalking(bool isWalking)
        {
            playerManager.SetWalking(isWalking);
        }

        private void CreateGrabJoint(InteractableItem item)
        {
            Vector3 grabPosition = item.GetGrabPosition(handTransform.position);

            grabJoint = gameObject.AddComponent<SpringJoint>();
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.connectedBody = item.Rigidbody;
            grabJoint.anchor = transform.InverseTransformPoint(handTransform.position);
            grabJoint.connectedAnchor = item.transform.InverseTransformPoint(grabPosition);
            grabJoint.spring = jointSpring;
            grabJoint.damper = jointDamper;
            grabJoint.minDistance = 0f;
            grabJoint.maxDistance = 0f;
            grabJoint.tolerance = 0.025f;
            grabJoint.enableCollision = false;
        }

        private void DestroyGrabJoint()
        {
            if (grabJoint == null) return;

            Destroy(grabJoint);
            grabJoint = null;
        }

        private void RotateHeldItem()
        {
            Rigidbody itemRigidbody = HeldItem.Rigidbody;
            if (itemRigidbody == null) return;

            Quaternion targetRotation = handTransform.rotation * Quaternion.Euler(HeldItem.HeldRotationOffset);
            Quaternion rotationDifference = targetRotation * Quaternion.Inverse(itemRigidbody.rotation);
            rotationDifference.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);

            if (angleInDegrees > 180f)
                angleInDegrees -= 360f;

            if (Mathf.Abs(angleInDegrees) <= 1f) return;

            Vector3 angularDisplacement = rotationAxis * (angleInDegrees * Mathf.Deg2Rad);
            Vector3 torque = angularDisplacement * rotateForce - itemRigidbody.angularVelocity * 10f;
            itemRigidbody.AddTorque(torque, ForceMode.Acceleration);
        }

        private void ThrowItem(InteractableItem item)
        {
            if (item.Rigidbody == null) return;

            item.Rigidbody.velocity = playerRigidbody.velocity + transform.forward * throwForce;
        }

        private void PulseInteractState()
        {
            playerManager.BeginInteract();
            playerManager.EndInteract();
        }

        private void ApplyCarryModifiers(InteractableItem item)
        {
            Transform lookTarget = item.ForceHolderFaceItem ? item.transform : null;
            playerManager.SetCarriedItem(item);
            playerManager.ApplyCarryModifiers(item.GetCurrentSpeedMultiplier(), lookTarget);
        }

        private static void RefreshHolderModifiers(InteractableItem item)
        {
            IReadOnlyList<PlayerInteractionManager> holders = item.Holders;
            for (int i = 0; i < holders.Count; i++)
                holders[i].ApplyCarryModifiers(item);
        }

        private void RefreshCurrentCandidate()
        {
            currentCandidate = null;

            float closestDistance = float.MaxValue;
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                InteractableItem item = candidates[i];
                if (item == null || item == HeldItem || !item.CanBeHeldBy(this))
                {
                    candidates.RemoveAt(i);
                    continue;
                }

                float distance = (item.transform.position - handTransform.position).sqrMagnitude;
                if (distance >= closestDistance) continue;

                closestDistance = distance;
                currentCandidate = item;
            }
        }

        private InteractableItem FindInteractableItem(Collider other)
        {
            if (((1 << other.gameObject.layer) & interactableMask.value) == 0)
                return null;

            return other.GetComponentInParent<InteractableItem>();
        }
    }
}
