using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Follows the mirrored head pose without inheriting armature scale or shear.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("TA/Adaptive Animation/Rigid Head Follower")]
    public sealed class CharacterRigidHeadFollower : MonoBehaviour
    {
        [SerializeField] private Transform sourceHead;
        [SerializeField] private Transform rigidRoot;

        public Transform SourceHead => sourceHead;
        public Transform RigidRoot => rigidRoot;

        public void Configure(Transform head, Transform root)
        {
            sourceHead = head;
            rigidRoot = root;
            SyncPose();
        }

        private void OnEnable() => SyncPose();
        private void LateUpdate() => SyncPose();

        public void SyncPose()
        {
            if (!sourceHead || !rigidRoot || sourceHead == rigidRoot) return;
            rigidRoot.SetPositionAndRotation(sourceHead.position, sourceHead.rotation);
        }
    }
}
