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
        private Component boundSource, metadata;
        private int bindingsFrame = -1;
        private GripPointProvider provider;
        private CarryAnimationLoad load;
        private VictimAI victim;
        private CylinderGripProvider cylinder;
        private BoxCollider box;
        private AdaptiveSceneProp prop;
        private Collider shape;

        // Monotonic diagnostics let tests/tools verify reuse without timing-dependent benchmarks.
        public int ContextRevision { get; private set; }
        public int BindingRefreshCount { get; private set; }

        [Header("Runtime context (read only)")]
        [SerializeField] private AdaptiveObjectType objectType;
        [SerializeField] private Vector3 objectSize;
        [SerializeField] private float objectWeightKg;
        [SerializeField] private float effectiveWeightKg;
        [SerializeField] private CarryRole role;
        [SerializeField] private AdaptiveInteractionType interactionType;
        [SerializeField] private InteractionPhase phase;
        [Tooltip("Diagnostic route hint; Animator commands remain owned by interaction controllers.")]
        [SerializeField] private string baseAnimationState;

        // Sample mutable inputs on access; unchanged reads reuse the published result.
        public InteractionContext Context { get { ReadContext(); return context; } }
        public AdaptationResult Result { get { ReadContext(); return result; } }

        private void OnEnable() => RefreshContext();
        private void Update() => ReadContext();
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
            // Explicit structural invalidation for same-frame component additions/reparenting.
            bindingsFrame = -1;
            ReadContext();
        }

        private void ReadContext()
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

            if (boundSource != source || bindingsFrame != Time.frameCount)
                BindSource(source, target != null);
            bool hasWeight = load && load.isActiveAndEnabled;
            float weight = hasWeight ? load.WeightKg : 0f;
            var coop = held as CooperativeCarryObject;
            float effective = hasWeight && coop ? coop.LoadPerPlayerKg : weight;
            var hoseGrip = source as CooperativeHoseGrip;
            var type = hoseGrip || provider is HoseGripProvider ? AdaptiveObjectType.Hose :
                victim ? AdaptiveObjectType.Survivor :
                provider is CylinderGripProvider ? AdaptiveObjectType.Cylinder :
                provider is BoxGripProvider || provider is CooperativeGripProvider || provider is ScenePropGripProvider
                    ? AdaptiveObjectType.Box : AdaptiveObjectType.Unknown;
            var interaction = target ? AdaptiveInteractionType.ObjectInteraction :
                type == AdaptiveObjectType.Hose ? AdaptiveInteractionType.FireHoseInteraction :
                type == AdaptiveObjectType.Survivor ? AdaptiveInteractionType.CarrySurvivor : AdaptiveInteractionType.CarryObject;
            var currentPhase = target ? InteractionPhase.ObjectInteraction : pending ? InteractionPhase.Preparing :
                actor.IsPlacingDown ? InteractionPhase.Placing : InteractionPhase.Carrying;
            var currentRole = held ? actor.CurrentCarryRole : CarryRole.None;
            Publish(new InteractionContext(actor, held, pending, target, source, type,
                Dimensions(), weight, effective, hasWeight, currentRole, interaction, currentPhase,
                provider, target ? objectController.ActiveAnimationName : string.Empty,
                actor.IsPickingUp, provider && provider.isActiveAndEnabled));
        }

        private void BindSource(Component source, bool objectInteraction)
        {
            boundSource = source;
            bindingsFrame = Time.frameCount;
            BindingRefreshCount++;
            // Lamp switch stays on the visual; normalized parent owns carry metadata.
            var parent = objectInteraction ? source.GetComponentInParent<AdaptiveSceneProp>() : null;
            metadata = parent ? (Component)parent : source;
            provider = metadata.GetComponent<GripPointProvider>();
            load = metadata.GetComponent<CarryAnimationLoad>();
            victim = source.GetComponent<VictimAI>();
            cylinder = metadata.GetComponent<CylinderGripProvider>();
            box = metadata.GetComponent<BoxCollider>();
            prop = metadata.GetComponent<AdaptiveSceneProp>();
            shape = metadata.GetComponent<Collider>();
            if (!shape) shape = metadata.GetComponentInChildren<Collider>();
        }

        private Vector3 Dimensions()
        {
            if (cylinder) return cylinder.ObjectDimensions;
            if (box) return Vector3.Scale(box.size, Abs(box.transform.lossyScale));
            if (prop) return Vector3.Scale(prop.Dimensions, Abs(metadata.transform.lossyScale));
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
            boundSource = metadata = null;
            bindingsFrame = -1;
            provider = null; load = null; victim = null;
            cylinder = null; box = null; prop = null; shape = null;
            Publish(default);
        }

        private void Publish(InteractionContext next)
        {
            if (ContextRevision != 0 && context.SameInputs(next)) return;
            context = next;
            result = ProceduralLayerProcessor.Process(next);
            ContextRevision++;
            UpdateDiagnostics();
        }

        private void UpdateDiagnostics()
        {
            objectType = context.ObjectType; objectSize = context.ObjectSize;
            objectWeightKg = context.ObjectWeightKg; effectiveWeightKg = context.EffectiveWeightKg;
            role = context.Role; interactionType = context.InteractionType; phase = context.Phase;
            baseAnimationState = result.AnimationStateHint;
        }
    }
}
