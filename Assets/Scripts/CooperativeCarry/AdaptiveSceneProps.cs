using System.Collections;
using System.Collections.Generic;
using HInteractions;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>
    /// Opt-in level adapter for imported props. Keeps source meshes, burning components and
    /// world poses intact, while giving the carry system unit-scale, upright physics roots.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class AdaptiveSceneProps : MonoBehaviour
    {
        [SerializeField] private Transform propsRoot;
        [SerializeField] private CapsuleCollider playerReference;
        [SerializeField] private CarryWeightSpeedProfile speedProfile;
        [SerializeField] private Transform[] additionalProps = new Transform[0];
        [SerializeField] private string[] fixedPropPrefixes = { "Props_FireTruck" };
        public readonly List<AdaptiveSceneProp> Props = new();
        public bool IsReady { get; private set; }

        public static ScenePropSize Classify(Vector3 size, float playerHeight)
        {
            float longest = Mathf.Max(size.x, size.y, size.z);
            return longest <= playerHeight * .3f ? ScenePropSize.Small :
                longest <= playerHeight * .55f ? ScenePropSize.Medium : ScenePropSize.Big;
        }

        private IEnumerator Start()
        {
            if (!propsRoot || !playerReference || !speedProfile)
            { Debug.LogError("AdaptiveSceneProps requires a level root, player collider and speed profile.", this); yield break; }
            var candidates = new List<Transform>();
            foreach (var item in propsRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!item.name.StartsWith("Props_") || item == propsRoot) continue;
                bool excluded = false;
                foreach (string prefix in fixedPropPrefixes)
                    if (item.name.StartsWith(prefix)) excluded = true;
                for (Transform p = item.parent; p && p != propsRoot; p = p.parent)
                    if (p.name.StartsWith("Props_") && p.GetComponentInChildren<Renderer>())
                    {
                        // Organizational containers are not physical props.
                        if (p.name != "Props_Static" && p.name != "Props_Interactive") excluded = true;
                    }
                if (!excluded && item.name != "Props_Static" && item.name != "Props_Interactive" &&
                    item.GetComponentInChildren<MeshRenderer>(true)) candidates.Add(item);
            }
            float playerHeight = playerReference.height * Mathf.Abs(playerReference.transform.lossyScale.y);
            foreach (var extra in additionalProps)
                if (extra && !candidates.Contains(extra) && extra.GetComponentInChildren<MeshRenderer>(true)) candidates.Add(extra);
            foreach (var visual in candidates) Prepare(visual, playerHeight);
            // Destroy() completes at end of frame; old child rigidbodies must be gone before pickup.
            yield return null;
            foreach (var prop in Props) prop.GetComponent<Liftable>().enabled = true;
            IsReady = true;
        }

        private void Prepare(Transform visual, float playerHeight)
        {
            bool wasActive = visual.gameObject.activeInHierarchy;
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var primary = renderers[0];
            Vector3 size = primary.localBounds.size;
            Vector3 longestHorizontal = Vector3.forward;
            float length = 0f;
            foreach (Vector3 axis in new[] { Vector3.right * size.x, Vector3.up * size.y, Vector3.forward * size.z })
            {
                Vector3 world = primary.transform.TransformVector(axis); world.y = 0f;
                if (world.sqrMagnitude > length) { length = world.sqrMagnitude; longestHorizontal = world; }
            }
            var root = new GameObject("Carry_" + visual.name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, visual.gameObject.scene);
            root.transform.SetPositionAndRotation(bounds.center, Quaternion.LookRotation(longestHorizontal.normalized, Vector3.up));
            // Root stays outside imported transform chains, which may contain nonuniform scale.
            var localBounds = new Bounds(); bool first = true;
            foreach (var renderer in renderers)
            {
                Bounds b = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { localBounds = new Bounds(p, Vector3.zero); first = false; } else localBounds.Encapsulate(p);
                }
            }
            root.transform.position = root.transform.TransformPoint(localBounds.center);
            Vector3 dimensions = localBounds.size;
            dimensions = Vector3.Max(dimensions, Vector3.one * .025f);
            bool team = Mathf.Max(dimensions.x, dimensions.z) > playerHeight * .65f ||
                (visual.name.ToLowerInvariant().Contains("bookshelf") && Mathf.Max(dimensions.x, dimensions.y, dimensions.z) > playerHeight * .5f);
            ScenePropSize category = team ? ScenePropSize.Big : Classify(dimensions, playerHeight);
            float weight = team ? 60f : category == ScenePropSize.Small ? 5f : category == ScenePropSize.Medium ? 15f : 30f;

            foreach (var old in visual.GetComponentsInChildren<Liftable>(true)) { old.enabled = false; Destroy(old); }
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true))
            { body.isKinematic = true; body.detectCollisions = false; Destroy(body); }
            visual.SetParent(root.transform, true);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = weight; rb.isKinematic = true; rb.useGravity = true;
            var shape = root.AddComponent<BoxCollider>(); shape.size = dimensions;
            // Keep existing colliders on the visual for heat detection and detailed collision.
            var colliders = visual.GetComponentsInChildren<Collider>(true);
            bool solid = false;
            foreach (var c in colliders)
            {
                if (c is MeshCollider mesh && !mesh.convex) mesh.convex = true;
                if (c.enabled && !c.isTrigger) solid = true;
            }
            shape.isTrigger = solid;
            root.AddComponent<CarryAnimationLoad>().Configure(weight);
            float gripY = Mathf.Min(0f, .55f - dimensions.y * .5f);
            Liftable liftable;
            if (team)
            {
                var carry = root.AddComponent<CooperativeCarryObject>();
                carry.ConfigureProp(speedProfile, gripY); liftable = carry;
            }
            else
            {
                liftable = root.AddComponent<Liftable>();
                var attachment = root.AddComponent<StableCarryAttachment>();
                // The placeholder's short forearms need the near face slightly behind CarryAnchor.
                attachment.ConfigureOffset(new Vector3(0f, -gripY, dimensions.z * .5f - .08f));
                attachment.DynamicOnRelease = true;
                root.AddComponent<CarryWeightMovement>().Configure(speedProfile);
            }
            liftable.enabled = false;
            root.AddComponent<ScenePropGripProvider>().Configure(shape, gripY, team);
            var metadata = root.AddComponent<AdaptiveSceneProp>(); metadata.Configure(category, dimensions, team, visual);
            Props.Add(metadata);
            root.SetActive(wasActive);
        }
    }
}
