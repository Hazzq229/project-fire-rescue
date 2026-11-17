using UnityEngine;

namespace HGame.Objects
{
    /// <summary>A solid door leaf that swings when a Rigidbody pushes it.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(HingeJoint))]
    public sealed class PushableDoor : MonoBehaviour
    {
        [SerializeField, Range(10f, 150f)] private float swingLimit = 105f;
        [SerializeField, Min(.1f)] private float leafMass = .6f;

        private void Awake()
        {
            var body = GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = false;
            body.mass = leafMass;
            body.angularDrag = 1.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var hinge = GetComponent<HingeJoint>();
            hinge.axis = Vector3.up;
            hinge.anchor = Vector3.zero;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.connectedBody = null;
            hinge.connectedAnchor = transform.position;
            hinge.useMotor = hinge.useSpring = false;
            hinge.limits = new JointLimits { min = -swingLimit, max = swingLimit };
            hinge.useLimits = true;
        }
    }
}
