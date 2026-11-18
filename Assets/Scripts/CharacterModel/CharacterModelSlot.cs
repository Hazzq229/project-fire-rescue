using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Serialized appearance configuration. The gameplay skeleton stays on the player.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("TA/Adaptive Animation/Character Model Slot")]
    public sealed class CharacterModelSlot : MonoBehaviour
    {
        [System.Serializable]
        public sealed class RigidHeadPart
        {
            public string rendererPath;
            public Mesh mesh;
            public Bounds localBounds;
        }

        // Authoring data only: Apply Model consumes this in the Editor. Runtime follows
        // the saved bones/meshes and never reads or rebuilds their vertices.
        [System.Serializable]
        public sealed class RigidHeadSetup
        {
            public GameObject model;
            public string modelDependencyHash;
            public string headPath, rootName;
            public Vector3 appearancePosition, appearanceScale, rootScale;
            public Quaternion appearanceRotation;
            public string headgearName;
            public float headgearScale, headgearLift;
            public RigidHeadPart[] parts;
        }

        [Tooltip("Existing visual container (IDLE). Keep its transform and Animator paths intact.")]
        public Transform modelRoot;
        [Tooltip("Common root bone of the existing skeleton (Bone).")]
        public Transform skeletonRoot;
        [Tooltip("Drag a model/FBX prefab with the same bone names, hierarchy and rest pose here.")]
        public GameObject replacementModel;
        [Header("Optional rigid headgear fit on the replacement")]
        public string headgearMeshName = "Topi_Pemadam";
        [Min(.1f)] public float headgearScale = 1f;
        [Tooltip("Lift in player world units; applied in the head bone's space.")]
        public float headgearLift;
        [HideInInspector] public GameObject appliedModel;
        [HideInInspector] public GameObject generatedVisuals;
        [HideInInspector] public GameObject[] boneAttachments;
        [HideInInspector] public Mesh[] generatedMeshes;
        [HideInInspector] public Renderer[] originalRenderers;
        [HideInInspector] public bool[] originalEnabled;
        [HideInInspector] public RigidHeadSetup savedRigidHead;

        private void Reset()
        {
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.rootBone) continue;
                skeletonRoot = skin.rootBone;
                modelRoot = skin.transform;
                while (modelRoot.parent && modelRoot.parent != transform) modelRoot = modelRoot.parent;
                break;
            }
        }
    }
}
