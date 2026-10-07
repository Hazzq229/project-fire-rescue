using System.Collections.Generic;
using UnityEngine;

namespace HInteractions
{
    [RequireComponent(typeof(Rigidbody))]
    public class Liftable : Interactable
    {
        [Header("Base Settings")]
        [SerializeField] protected int maxHolders = 1; // Default 1, bisa di-override
        [field: SerializeField] public Vector3 LiftDirectionOffset { get; private set; } = Vector3.zero;

        public Rigidbody Rigidbody { get; protected set; }

        // List object holder
        public List<IObjectHolder> Holders { get; protected set; } = new List<IObjectHolder>();
        public bool IsLifted => Holders.Count > 0;
        public bool SupportsStableSingleCarry => maxHolders == 1;

        [Header("Movement Settings")]
        [SerializeField] protected float defaultSpeedPenalty = 0f;
        [SerializeField] protected bool defaultForceFaceObject = false;

        // getter that can be accessed from another script
        public virtual float SpeedPenalty => defaultSpeedPenalty;
        public virtual bool ForceFaceObject => defaultForceFaceObject;
        protected List<(GameObject obj, int defaultLayer)> _defaultLayers = new();
        private float _defaultDrag;
        private float _defaultAngularDrag;

        protected override void Awake()
        {
            base.Awake();
            Rigidbody = GetComponent<Rigidbody>();
            _defaultDrag = Rigidbody.drag;
            _defaultAngularDrag = Rigidbody.angularDrag;
        }

        public bool CanBePickedUp() => Holders.Count < maxHolders;

        public virtual Vector3 GetGrabPoint(Vector3 handPosition)
        {
            return transform.position; // default based on gameobject origin point 
        }

        public virtual void PickUp(IObjectHolder holder, int layer)
        {
            if (!CanBePickedUp() || Holders.Contains(holder)) return;

            Holders.Add(holder);

            if (Holders.Count == 1)
            {
                SaveAndChangeLayers(layer);
                OnFirstPickup();
            }

            OnAnyPickup();
        }

        public virtual void Drop(IObjectHolder holder)
        {
            if (!Holders.Contains(holder)) return;

            Holders.Remove(holder);

            if (Holders.Count == 0)
            {
                RevertLayers();
                OnAllDropped();
            }

            OnAnyDrop();
        }
        protected virtual void OnFirstPickup()
        {
            Rigidbody.useGravity = false;
            Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            Rigidbody.drag = 10f;
            Rigidbody.angularDrag = 10f;
        }
        protected virtual void OnAllDropped()
        {
            Rigidbody.useGravity = true;
            Rigidbody.interpolation = RigidbodyInterpolation.None;
            Rigidbody.drag = _defaultDrag;
            Rigidbody.angularDrag = _defaultAngularDrag;
        }
        protected virtual void OnAnyPickup() { }   // Saat ada orang baru join angkat
        protected virtual void OnAnyDrop() { }     // Saat ada satu orang lepas

        // Helper Methods

        protected void SaveAndChangeLayers(int newLayer)
        {
            _defaultLayers.Clear();
            foreach (Collider col in gameObject.GetComponentsInChildren<Collider>())
            {
                _defaultLayers.Add((col.gameObject, col.gameObject.layer));
                col.gameObject.layer = newLayer;
            }
        }

        protected void RevertLayers()
        {
            foreach (var item in _defaultLayers)
                item.obj.layer = item.defaultLayer;
        }
    }
}