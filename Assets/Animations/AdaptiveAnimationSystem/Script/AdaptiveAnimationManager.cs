using HInteractions;
using HPlayer;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractionController))]
    [DefaultExecutionOrder(160)]
    [AddComponentMenu("TA/Adaptive Animation/Adaptive Animation Manager")]
    public sealed class AdaptiveAnimationManager : MonoBehaviour
    {
        private InteractionController actor;
        private DoorInteractionController objectController;
        private InteractionContext context;
        private AdaptationResult result;

        [Header("Runtime context (read only)")]
        [SerializeField] private AdaptiveObjectType objectType;
        [SerializeField] private Vector3 objectSize;
        [SerializeField] private float objectWeightKg;
        [SerializeField] private float effectiveWeightKg;
        [SerializeField] private CarryRole role;
        [SerializeField] private AdaptiveInteractionType interactionType;
        [SerializeField] private InteractionPhase phase;
        [SerializeField] private string baseAnimationState;

        // Refresh on access as well as Update: ownership may change later in the
        // same frame, and callers must not receive a cached pending/previous target.
        public InteractionContext Context { get { RefreshContext(); return context; } }
        public AdaptationResult Result { get { RefreshContext(); return result; } }

        private void OnEnable() => RefreshContext();
        private void Update() => RefreshContext();
        private void OnDisable() => Clear();

        public static bool TryRead(InteractionController controller, out AdaptationResult adaptation)
        {
            var manager = controller ? controller.GetComponent<AdaptiveAnimationManager>() : null;
            if (manager && manager.isActiveAndEnabled)
            {
                adaptation = manager.Result;
                return true;
            }
            adaptation = default;
            return false;
        }

        public void RefreshContext()
        {
            if (!actor) actor = GetComponent<InteractionController>();
            if (!objectController) objectController = GetComponent<DoorInteractionController>();
            if (!isActiveAndEnabled || !actor || !actor.isActiveAndEnabled)
            { Clear(); return; }

            var held = actor.HeldObject && actor.HeldObject.isActiveAndEnabled ? actor.HeldObject : null;
            var pending = !held && actor.PendingPickupObject && actor.PendingPickupObject.isActiveAndEnabled
                ? actor.PendingPickupObject : null;
            var target = !held && !pending && objectController && objectController.isActiveAndEnabled
                ? objectController.ActiveTarget : null;
            if (target && !target.isActiveAndEnabled) target = null;
            Component source = held ? (Component)held : pending ? (Component)pending : target;
            if (!source) { Clear(); return; }

            // An adapted lamp keeps its switch component on the original visual,
            // while its size/load/provider belong to the normalized carry root.
            var adaptedParent = target ? source.GetComponentInParent<AdaptiveSceneProp>() : null;
            Component metadata = adaptedParent ? (Component)adaptedParent : source;
            var provider = metadata.GetComponent<GripPointProvider>();
            var load = metadata.GetComponent<CarryAnimationLoad>();
            bool hasWeight = load && load.isActiveAndEnabled;
            float weight = hasWeight ? load.WeightKg : 0f;
            var coop = held as CooperativeCarryObject;
            float effective = hasWeight && coop ? coop.LoadPerPlayerKg : weight;
            var hoseGrip = source as CooperativeHoseGrip;
            var type = hoseGrip || provider is HoseGripProvider ? AdaptiveObjectType.Hose :
                source.GetComponent<VictimAI>() ? AdaptiveObjectType.Survivor :
                provider is CylinderGripProvider ? AdaptiveObjectType.Cylinder :
                provider is BoxGripProvider || provider is CooperativeGripProvider || provider is ScenePropGripProvider
                    ? AdaptiveObjectType.Box : AdaptiveObjectType.Unknown;
            var interaction = target ? AdaptiveInteractionType.ObjectInteraction :
                type == AdaptiveObjectType.Hose ? AdaptiveInteractionType.FireHoseInteraction :
                type == AdaptiveObjectType.Survivor ? AdaptiveInteractionType.CarrySurvivor : AdaptiveInteractionType.CarryObject;
            var currentPhase = target ? InteractionPhase.ObjectInteraction : pending ? InteractionPhase.Preparing :
                actor.IsPlacingDown ? InteractionPhase.Placing : InteractionPhase.Carrying;
            var currentRole = held ? actor.CurrentCarryRole : CarryRole.None;
            context = new InteractionContext(actor, held, pending, target, source, type,
                Dimensions(metadata), weight, effective, hasWeight, currentRole, interaction, currentPhase,
                provider, target ? objectController.ActiveAnimationName : string.Empty);
            result = ProceduralLayerProcessor.Process(context);
            UpdateDiagnostics();
        }

        private static Vector3 Dimensions(Component source)
        {
            var cylinder = source.GetComponent<CylinderGripProvider>();
            if (cylinder) return cylinder.ObjectDimensions;
            var box = source.GetComponent<BoxCollider>();
            if (box) return Vector3.Scale(box.size, Abs(box.transform.lossyScale));
            var prop = source.GetComponent<AdaptiveSceneProp>();
            if (prop) return Vector3.Scale(prop.Dimensions, Abs(source.transform.lossyScale));
            var shape = source.GetComponent<Collider>();
            if (!shape) shape = source.GetComponentInChildren<Collider>();
            if (!shape) return Vector3.zero;
            // Express the shape in its own axes, never a yaw-dependent world AABB.
            if (shape is CapsuleCollider capsule)
            {
                Vector3 size = Vector3.one * capsule.radius * 2f;
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                return Vector3.Scale(size, Abs(capsule.transform.lossyScale));
            }
            if (shape is BoxCollider childBox) return Vector3.Scale(childBox.size, Abs(childBox.transform.lossyScale));
            if (shape is SphereCollider sphere) return Abs(sphere.transform.lossyScale) * sphere.radius * 2f;
            if (shape is MeshCollider mesh && mesh.sharedMesh)
                return Vector3.Scale(mesh.sharedMesh.bounds.size, Abs(mesh.transform.lossyScale));
            return Vector3.zero;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private void Clear()
        {
            context = default;
            result = ProceduralLayerProcessor.Process(context);
            UpdateDiagnostics();
        }

        private void UpdateDiagnostics()
        {
            objectType = context.ObjectType; objectSize = context.ObjectSize;
            objectWeightKg = context.ObjectWeightKg; effectiveWeightKg = context.EffectiveWeightKg;
            role = context.Role; interactionType = context.InteractionType; phase = context.Phase;
            baseAnimationState = result.BaseAnimationState;
        }
    }
}
