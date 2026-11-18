using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Fits an instance's rigid skinned accessory without changing the imported mesh.</summary>
    [ExecuteAlways]
    public sealed class CharacterHeadgearFit : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer target;
        [SerializeField] private Mesh source;
        [SerializeField] private int boneIndex;
        [SerializeField] private float fitScale = 1f;
        [SerializeField] private Vector3 boneLocalLift;
        private Mesh fitted;

        public void Configure(SkinnedMeshRenderer renderer, int bone, float scale, Vector3 worldLift)
        {
            Release();
            target = renderer;
            source = renderer.sharedMesh;
            boneIndex = bone;
            fitScale = scale;
            boneLocalLift = renderer.bones[bone].InverseTransformVector(worldLift);
            Rebuild();
        }

        private void OnEnable() => Rebuild();
        private void OnDisable() => Release();

        private void Rebuild()
        {
            Release();
            if (!target || !source || boneIndex < 0 || boneIndex >= source.bindposes.Length) return;
            fitted = Instantiate(source);
            fitted.name = source.name + " (instance fit)";
            fitted.hideFlags = HideFlags.HideAndDontSave;
            var vertices = source.vertices;
            var bind = source.bindposes[boneIndex];
            var inverse = bind.inverse;
            var bounds = new Bounds(bind.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(bind.MultiplyPoint3x4(vertex));
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = inverse.MultiplyPoint3x4(bounds.center +
                    (bind.MultiplyPoint3x4(vertices[i]) - bounds.center) * fitScale + boneLocalLift);
            fitted.vertices = vertices;
            fitted.RecalculateBounds();
            target.sharedMesh = fitted;
        }

        private void Release()
        {
            if (!fitted) return;
            if (target && target.sharedMesh == fitted) target.sharedMesh = source;
            if (Application.isPlaying) Destroy(fitted);
            else DestroyImmediate(fitted);
            fitted = null;
        }
    }
}
