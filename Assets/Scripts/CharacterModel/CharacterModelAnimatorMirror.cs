using System;
using System.Collections.Generic;
using UnityEngine;

namespace TA.AdaptiveAnimation
{
    /// <summary>Synchronizes a replacement model's skeleton with the gameplay skeleton.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterModelAnimatorMirror : MonoBehaviour
    {
        [Serializable]
        public struct BonePair
        {
            public Transform source;
            public Transform target;
        }

        [SerializeField] Animator sourceAnimator;
        [SerializeField] BonePair[] bonePairs;

        public void Configure(Animator source, Transform sourceSkeletonRoot, Transform replacementSkeletonRoot)
        {
            sourceAnimator = source;
            if (!sourceSkeletonRoot || !replacementSkeletonRoot)
            {
                bonePairs = Array.Empty<BonePair>();
                return;
            }

            var replBones = replacementSkeletonRoot.GetComponentsInChildren<Transform>(true);
            var replMap = new Dictionary<string, Transform>(replBones.Length);
            foreach (var b in replBones)
            {
                if (!replMap.ContainsKey(b.name))
                    replMap.Add(b.name, b);
            }

            var srcBones = sourceSkeletonRoot.GetComponentsInChildren<Transform>(true);
            var pairs = new List<BonePair>(srcBones.Length);
            foreach (var s in srcBones)
            {
                if (replMap.TryGetValue(s.name, out var target))
                {
                    pairs.Add(new BonePair { source = s, target = target });
                }
            }

            bonePairs = pairs.ToArray();
        }

        void LateUpdate()
        {
            if (bonePairs == null || bonePairs.Length == 0) return;

            for (var i = 0; i < bonePairs.Length; i++)
            {
                var pair = bonePairs[i];
                if (pair.source && pair.target)
                {
                    pair.target.localRotation = pair.source.localRotation;
                }
            }
        }
    }
}
