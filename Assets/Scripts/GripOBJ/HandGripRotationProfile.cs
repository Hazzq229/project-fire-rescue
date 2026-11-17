using UnityEngine;

namespace TA.AdaptiveAnimation
{
    [CreateAssetMenu(fileName = "HandGripRotationProfile", menuName = "TA/Adaptive Animation/Hand Grip Rotation Profile")]
    public sealed class HandGripRotationProfile : ScriptableObject
    {
        [SerializeField] private bool calibrated;
        [SerializeField] private string gripStyleId;
        [SerializeField] private Quaternion leftCorrection = Quaternion.identity;
        [SerializeField] private Quaternion rightCorrection = Quaternion.identity;
        public bool Matches(string style) => calibrated && !string.IsNullOrEmpty(style) && gripStyleId == style;
        public Quaternion LeftCorrection => leftCorrection;
        public Quaternion RightCorrection => rightCorrection;

        public void Record(string style, Quaternion leftFrame, Quaternion rightFrame,
            Quaternion leftTarget, Quaternion rightTarget)
        {
            gripStyleId = style;
            leftCorrection = Quaternion.Inverse(leftFrame) * leftTarget;
            rightCorrection = Quaternion.Inverse(rightFrame) * rightTarget;
            calibrated = true;
        }
    }
}
