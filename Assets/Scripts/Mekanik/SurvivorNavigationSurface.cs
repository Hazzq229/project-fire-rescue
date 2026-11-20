using System.Collections;
using System.Collections.Generic;
using HGame.Objects;
using HInteractions;
using HPhysic;
using TA.AdaptiveAnimation;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class SurvivorNavigationSurface : MonoBehaviour
{
    [SerializeField] private NavMeshData navigationData;

    private readonly List<NavMeshObstacle> addedObstacles = new List<NavMeshObstacle>();
    private NavMeshDataInstance instance;

    private void OnEnable()
    {
        if (!navigationData) return;
        instance = NavMesh.AddNavMeshData(navigationData);
        StartCoroutine(RegisterObstacles());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (instance.valid) instance.Remove();
        foreach (var obstacle in addedObstacles)
        {
            if (!obstacle) continue;
            obstacle.enabled = false;
            Destroy(obstacle);
        }
        addedObstacles.Clear();
    }

    private static bool IsDoorLeaf(Collider collider) =>
        collider.attachedRigidbody && (collider.GetComponentInParent<DoorInteractable>() ||
            collider.GetComponentInParent<PushableDoor>());

    private IEnumerator RegisterObstacles()
    {
        // AdaptiveSceneProps wraps the imported furniture during startup.
        // Wait a frame for the old child rigidbodies to be removed.
        yield return null;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (!root.activeInHierarchy) continue;
            foreach (var box in root.GetComponentsInChildren<BoxCollider>())
            {
                if (!ShouldCarve(box) || box.GetComponent<NavMeshObstacle>()) continue;
                var obstacle = box.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = box.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                addedObstacles.Add(obstacle);
            }
        }
    }

    private static bool ShouldCarve(BoxCollider box)
    {
        if (!box.enabled) return false;
        if (!box.isTrigger && IsDoorLeaf(box)) return true;
        if (box.bounds.size.y < .18f) return false;

        // The normalized root proxy covers the whole prop even when its child
        // has the solid collider. Use only this one shape per adapted object.
        if (box.TryGetComponent<AdaptiveSceneProp>(out _)) return true;
        if (box.isTrigger || !box.TryGetComponent(out Liftable liftable)) return false;
        return !box.GetComponent<VictimAI>() && !(liftable is CooperativeHoseGrip) &&
            !(liftable is PhysicCableCon) && !box.GetComponent<HoseWaterController>();
    }
}
