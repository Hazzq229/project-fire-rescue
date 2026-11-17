using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [CreateAssetMenu(fileName = "ObjectGripOrientation", menuName = "TA/Adaptive Animation/Box Grip Orientation Preset")]
    public sealed class BoxGripOrientationPreset : ScriptableObject
    {
        [SerializeField] private bool calibrated;
        [SerializeField] private Quaternion localRotationA = Quaternion.identity;
        [SerializeField] private Quaternion localRotationB = Quaternion.identity;
        public bool Calibrated => calibrated;
        public Quaternion LocalRotationA => localRotationA;
        public Quaternion LocalRotationB => localRotationB;

        public void Record(Quaternion objectRotation, Quaternion frameA, Quaternion frameB)
        {
            localRotationA = Quaternion.Inverse(objectRotation) * frameA;
            localRotationB = Quaternion.Inverse(objectRotation) * frameB;
            calibrated = true;
        }
    }
}
