using System.Collections.Generic;
using UnityEngine;

namespace HGame.Objects
{
    /// <summary>Shared contract for stationary, animation-driven object interactions.</summary>
    public abstract class ObjectInteractionTarget : MonoBehaviour
    {
        public static readonly HashSet<ObjectInteractionTarget> ActiveTargets = new();
        public abstract bool ActiveState { get; }
        public abstract bool IsMoving { get; }
        public abstract bool Available { get; }
        public virtual int InteractionPointCount => 1;
        public abstract Transform GetInteractionPoint(int index);
        // Null uses the legacy door push/pull mapping stored on the player.
        public virtual string AnimationFor(bool nextState, int pointIndex) => null;
        public abstract bool TryReserve(MonoBehaviour requester, bool nextState);
        public abstract bool PerformReservedAction(MonoBehaviour requester);
        public abstract void Release(MonoBehaviour requester);
        protected virtual void OnEnable() => ActiveTargets.Add(this);
        protected virtual void OnDisable() => ActiveTargets.Remove(this);
    }
}
