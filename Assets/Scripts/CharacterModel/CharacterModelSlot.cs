using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Serialized appearance configuration. The gameplay skeleton stays on the player.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("TA/Adaptive Animation/Character Model Slot")]
    public sealed class CharacterModelSlot : MonoBehaviour
    {
        [Tooltip("Existing visual container (IDLE). Keep its transform and Animator paths intact.")]
        public Transform modelRoot;
        [Tooltip("Common root bone of the existing skeleton (Bone).")]
        public Transform skeletonRoot;
        [Tooltip("Drag a model/FBX prefab with the same bone names, hierarchy and rest pose here.")]
        public GameObject replacementModel;
        [HideInInspector] public GameObject appliedModel;
        [HideInInspector] public GameObject generatedVisuals;
        [HideInInspector] public GameObject[] boneAttachments;
        [HideInInspector] public Renderer[] originalRenderers;
        [HideInInspector] public bool[] originalEnabled;

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
