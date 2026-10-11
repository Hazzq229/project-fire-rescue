using System;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    public enum GripPresetSize { Small, Medium, Big }

    [CreateAssetMenu(fileName = "SharedGripOrientations", menuName = "TA/Adaptive Animation/Shared Grip Orientation Profile")]
    public sealed class SharedGripOrientationProfile : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public bool recorded;
            public Quaternion localRotationA;
            public Quaternion localRotationB;
        }
        [SerializeField] private Entry small;
        [SerializeField] private Entry medium;
        [SerializeField] private Entry big;

        public bool TryGet(GripPresetSize preset, out Quaternion a, out Quaternion b)
        {
            Entry entry;
            switch (preset)
            {
                case GripPresetSize.Small: entry = small; break;
                case GripPresetSize.Medium: entry = medium; break;
                case GripPresetSize.Big: entry = big; break;
                default: a = b = Quaternion.identity; return false;
            }
            a = entry.recorded ? entry.localRotationA : Quaternion.identity;
            b = entry.recorded ? entry.localRotationB : Quaternion.identity;
            return entry.recorded;
        }

        // Hanya slot terpilih yang berubah. Dua slot lainnya dipertahankan.
        public void Record(GripPresetSize preset, Quaternion a, Quaternion b)
        {
            var entry = new Entry { recorded = true, localRotationA = a, localRotationB = b };
            switch (preset)
            {
                case GripPresetSize.Small: small = entry; break;
                case GripPresetSize.Medium: medium = entry; break;
                case GripPresetSize.Big: big = entry; break;
            }
        }
    }
}
