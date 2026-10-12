using UnityEngine;

namespace HGame.Objects
{
    [DisallowMultipleComponent]
    [AddComponentMenu("TA/Adaptive Animation/Interactable Lamp")]
    public sealed class LampInteractable : ObjectInteractionTarget
    {
        [SerializeField] private Transform interactionPoint;
        [SerializeField] private Light lampLight;
        [SerializeField] private Renderer bulbRenderer;
        [SerializeField] private bool initiallyOn = true;
        [SerializeField] private string switchAnimation = "DoorPush";
        [SerializeField] private Color onColor = new Color(1f, .76f, .32f, 1f);
        [SerializeField] private Color offColor = new Color(.16f, .13f, .09f, 1f);
        private MaterialPropertyBlock properties;
        private MonoBehaviour owner;
        private bool reservedState, performed;
        public bool IsOn { get; private set; }
        public override bool ActiveState => IsOn;
        public override bool IsMoving => false;
        public override bool Available => isActiveAndEnabled && interactionPoint && lampLight && !owner;
        public override Transform GetInteractionPoint(int index) => index == 0 ? interactionPoint : null;
        public override string AnimationFor(bool nextState, int pointIndex) => switchAnimation;

        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            SetState(initiallyOn);
        }

        public override bool TryReserve(MonoBehaviour requester, bool nextState)
        {
            if (!requester || !Available || nextState == IsOn) return false;
            owner = requester;
            reservedState = nextState;
            performed = false;
            return true;
        }

        public override bool PerformReservedAction(MonoBehaviour requester)
        {
            if (!isActiveAndEnabled || !lampLight || !owner || owner != requester || performed) return false;
            performed = true;
            SetState(reservedState);
            return true;
        }

        private void SetState(bool value)
        {
            IsOn = value;
            if (lampLight) lampLight.enabled = value;
            if (!bulbRenderer) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            bulbRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", value ? onColor : offColor);
            properties.SetColor("_Color", value ? onColor : offColor);
            properties.SetColor("_EmissionColor", value ? onColor * 2f : Color.black);
            bulbRenderer.SetPropertyBlock(properties);
        }

        public override void Release(MonoBehaviour requester)
        {
            if (owner == requester) owner = null;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            owner = null;
        }
    }
}
