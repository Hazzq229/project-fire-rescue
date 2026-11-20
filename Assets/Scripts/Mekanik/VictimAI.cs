using System.Collections.Generic;
using HInteractions;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody), typeof(Liftable))]
public class VictimAI : MonoBehaviour
{
    public enum VictimState { Calm, Panic, Cower, Dead, Walking, Downed, Recovering, Rescued }
    public VictimState currentState = VictimState.Calm;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    [Header("Movement")]
    [SerializeField] private NavMeshAgent navigationAgent;
    [Min(0f)] public float panicSpeed = 2f;
    [Tooltip("Horizontal search distance for casual patrol destinations.")]
    [Min(0f)] public float radiusX = 3f;
    [Min(0f)] public float radiusZ = 3f;
    [SerializeField, Min(0f)] private float walkSpeed = .8f;
    [SerializeField, Min(0f)] private float panicRadius = 4f;
    [SerializeField, Min(.1f)] private float idleDuration = 2.5f;
    [SerializeField, Min(.1f)] private float walkDuration = 1.5f;

    [Header("Fire exposure")]
    [Tooltip("Heat required to knock down the survivor.")]
    [Min(.1f)] public float maxHealth = 100f;
    [Tooltip("How far heat reaches beyond the flame collider.")]
    [SerializeField, Min(0f)] private float fireHeatRange = .9f;
    [Tooltip("Seconds in full heat after collapse before the survivor dies. Weaker heat takes longer.")]
    [SerializeField, Min(.1f)] private float downedFireExposureSeconds = 7f;
    [SerializeField, Min(0f)] private float healthRecoveryPerSecond = 1f;
    [HideInInspector] public bool isSafe;

    private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
    private static readonly int Walk = Animator.StringToHash("Base Layer.Walk");
    private static readonly int Panic = Animator.StringToHash("Base Layer.Panic");
    private static readonly int Collapse = Animator.StringToHash("Base Layer.Collapse");
    private static readonly int Recover = Animator.StringToHash("Base Layer.Recover");
    private static readonly float[] FleeHeadings = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
    private const float RouteCheckInterval = .75f;
    private const float NearDestination = .20f;

    private readonly HashSet<SafeZoneTrigger> safeZones = new HashSet<SafeZoneTrigger>();
    private Rigidbody body;
    private CapsuleCollider capsule;
    private Liftable liftable;
    private Vector3 home;
    private NavMeshPath nextPath;
    private Vector3 lastRoutePosition;
    private float nextRouteCheck;
    private float nextBindAttempt;
    private float currentHealth;
    private float downedExposure;
    private float lastReleaseTime = float.NegativeInfinity;
    private float calmUntil;
    private int pose;
    private int fleeHeadingOffset;
    private bool wasHeld;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        liftable = GetComponent<Liftable>();
        home = body.position;
        currentHealth = maxHealth;
        calmUntil = Time.time + idleDuration;
        if (animator) animator.applyRootMotion = false;
        if (navigationAgent)
        {
            // Rigidbody stays authoritative so pickup, gravity and trigger interactions
            // continue to use the same body. The agent provides steering only.
            navigationAgent.updatePosition = false;
            navigationAgent.updateRotation = false;
            navigationAgent.enabled = false;
            nextPath = new NavMeshPath();
        }
        ShowState(currentState);
    }

    private void Update()
    {
        if (!animator || animator.IsInTransition(0)) return;
        var animatedState = animator.GetCurrentAnimatorStateInfo(0);
        if ((currentState == VictimState.Downed || currentState == VictimState.Dead) &&
            animatedState.fullPathHash == Collapse &&
            animatedState.normalizedTime >= 1f)
            animator.speed = 0f;
        else if (currentState == VictimState.Recovering &&
                 animatedState.fullPathHash == Recover && animatedState.normalizedTime >= 1f)
            ShowState(VictimState.Rescued);
    }

    private void FixedUpdate()
    {
        if (liftable.IsLifted)
        {
            SuspendNavigation();
            wasHeld = true;
            if (currentState != VictimState.Downed && currentState != VictimState.Dead)
                ShowState(VictimState.Cower);
            return;
        }

        if (wasHeld)
        {
            home = body.position;
            wasHeld = false;
            lastReleaseTime = Time.fixedTime;
            calmUntil = Time.time + idleDuration;
        }

        // A released body can take a few physics steps to settle into the trigger.
        if (animator && Time.fixedTime - lastReleaseTime <= .4f && !isSafe &&
            currentState != VictimState.Dead && safeZones.Count > 0)
        {
            safeZones.RemoveWhere(zone => !zone || !zone.isActiveAndEnabled);
            if (safeZones.Count > 0)
            {
                isSafe = true;
                liftable.enabled = false;
                ShowState(currentState == VictimState.Downed && animator.HasState(0, Recover)
                    ? VictimState.Recovering : VictimState.Rescued);
            }
        }

        if (!isSafe && currentState != VictimState.Dead) UpdateFireExposure();

        if (isSafe || currentState == VictimState.Downed || currentState == VictimState.Dead)
        {
            SuspendNavigation();
            DriveBody(Vector3.zero);
            return;
        }

        if (!BindNavigation())
        {
            if (currentState != VictimState.Calm) BeginIdle();
            DriveBody(Vector3.zero);
            return;
        }
        SyncNavigationPosition();

        if (Fire.TryGetClosestActive(body.position, panicRadius, out var firePosition))
        {
            bool enteringPanic = currentState != VictimState.Panic;
            if (enteringPanic) ShowState(VictimState.Panic);
            bool stalled = false;
            if (enteringPanic || NeedsNewRoute(out stalled))
            {
                // A moving obstacle may stop the body before its carve updates.
                fleeHeadingOffset = stalled ? (fleeHeadingOffset + 1) % FleeHeadings.Length : 0;
                StartRouteCheck();
                navigationAgent.ResetPath();
                TryFlee(firePosition);
            }
            DriveBody(navigationAgent.desiredVelocity);
            return;
        }

        if (currentState != VictimState.Calm && currentState != VictimState.Walking)
        {
            home = body.position;
            BeginIdle();
        }

        if (currentState == VictimState.Walking)
        {
            if (NeedsNewRoute(out bool stalled) && ((!stalled && Time.time >= calmUntil) || !TryPatrol()))
                BeginIdle();
        }
        else if (Time.time >= calmUntil)
        {
            if (walkSpeed > 0f && TryPatrol())
            {
                calmUntil = Time.time + walkDuration;
                ShowState(VictimState.Walking);
            }
            else calmUntil = Time.time + idleDuration;
        }

        DriveBody(currentState == VictimState.Walking ? navigationAgent.desiredVelocity : Vector3.zero);
    }

    private void BeginIdle()
    {
        if (navigationAgent && navigationAgent.enabled && navigationAgent.isOnNavMesh)
            navigationAgent.ResetPath();
        calmUntil = Time.time + idleDuration;
        ShowState(VictimState.Calm);
    }

    private NavMeshQueryFilter NavigationFilter => new NavMeshQueryFilter
    {
        agentTypeID = navigationAgent.agentTypeID,
        areaMask = navigationAgent.areaMask
    };

    private Vector3 Feet => body.position - Vector3.up * navigationAgent.baseOffset;

    private bool BindNavigation()
    {
        if (!navigationAgent || body.isKinematic) return false;
        if (navigationAgent.enabled)
        {
            if (navigationAgent.isOnNavMesh) return true;
            SuspendNavigation();
        }
        if (Time.fixedTime < nextBindAttempt) return false;
        nextBindAttempt = Time.fixedTime + .3f;

        // Retry only when the released body is close to the actual walkable
        // surface. Sampling never moves a victim onto it or through a wall.
        if (!NavMesh.SamplePosition(Feet, out var hit, .45f, NavigationFilter) ||
            HorizontalDistanceSquared(hit.position, Feet) > .18f * .18f)
            return false;

        navigationAgent.enabled = true;
        if (navigationAgent.isOnNavMesh) return true;
        SuspendNavigation();
        return false;
    }

    private void SuspendNavigation()
    {
        if (navigationAgent && navigationAgent.enabled) navigationAgent.enabled = false;
    }

    private void SyncNavigationPosition()
    {
        var position = body.position;
        navigationAgent.nextPosition = new Vector3(position.x, navigationAgent.nextPosition.y, position.z);
    }

    private bool NeedsNewRoute(out bool stalled)
    {
        stalled = false;
        if (navigationAgent.pathPending || Time.fixedTime < nextRouteCheck) return false;
        stalled = navigationAgent.hasPath && HorizontalDistanceSquared(body.position, lastRoutePosition) < .08f * .08f;
        StartRouteCheck();
        return stalled || !navigationAgent.hasPath || navigationAgent.isPathStale ||
            navigationAgent.pathStatus != NavMeshPathStatus.PathComplete ||
            navigationAgent.remainingDistance <= Mathf.Max(NearDestination, navigationAgent.stoppingDistance);
    }

    private void StartRouteCheck()
    {
        lastRoutePosition = body.position;
        nextRouteCheck = Time.fixedTime + RouteCheckInterval;
    }

    private bool TryPatrol()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle;
            Vector3 destination = new Vector3(home.x + offset.x * radiusX, Feet.y, home.z + offset.y * radiusZ);
            if (TrySetRoute(destination, walkSpeed)) return true;
        }
        return false;
    }

    private void TryFlee(Vector3 firePosition)
    {
        Vector3 away = body.position - firePosition;
        away.y = 0f;
        if (away.sqrMagnitude < .0001f) away = transform.forward;
        away.Normalize();
        float distance = Mathf.Max(2.5f, panicRadius);
        for (int i = 0; i < FleeHeadings.Length; i++)
        {
            float angle = FleeHeadings[(i + fleeHeadingOffset) % FleeHeadings.Length];
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
            if (TrySetRoute(Feet + direction * distance, panicSpeed)) return;
            if (TrySetRoute(Feet + direction * (distance * .5f), panicSpeed)) return;
        }
    }

    private bool TrySetRoute(Vector3 destination, float speed)
    {
        if (!NavMesh.SamplePosition(destination, out var hit, .75f, NavigationFilter) ||
            HorizontalDistanceSquared(hit.position, body.position) < .50f * .50f ||
            !navigationAgent.CalculatePath(hit.position, nextPath) ||
            nextPath.status != NavMeshPathStatus.PathComplete ||
            !navigationAgent.SetPath(nextPath)) return false;

        navigationAgent.speed = speed;
        StartRouteCheck();
        return true;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        var delta = first - second;
        return delta.x * delta.x + delta.z * delta.z;
    }

    private void DriveBody(Vector3 direction)
    {
        if (body.isKinematic) return;
        var velocity = body.velocity;
        direction.y = 0f;
        body.velocity = new Vector3(direction.x, velocity.y, direction.z);
        if (direction.sqrMagnitude > .0001f)
        {
            var facing = Quaternion.LookRotation(direction, Vector3.up);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, facing, 540f * Time.fixedDeltaTime));
        }
    }

    private void ShowState(VictimState state)
    {
        currentState = state;
        if (!animator || !animator.runtimeAnimatorController) return;
        int nextPose = state == VictimState.Walking ? Walk :
            state == VictimState.Panic ? Panic :
            state == VictimState.Downed || state == VictimState.Dead ? Collapse :
            state == VictimState.Recovering ? Recover : Idle;
        if (pose == nextPose || !animator.HasState(0, nextPose)) return;
        pose = nextPose;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(pose, .10f, 0);
    }

    private void UpdateFireExposure()
    {
        float heatRate = Fire.GetSurvivorHeat(capsule, fireHeatRange, out float exposureRate);
        if (currentState == VictimState.Downed)
        {
            downedExposure += exposureRate * Time.fixedDeltaTime;
            if (downedExposure >= downedFireExposureSeconds) DieFromFire();
            return;
        }

        if (heatRate > 0f)
            ApplyFireDamage(heatRate * Time.fixedDeltaTime);
        else
            currentHealth = Mathf.MoveTowards(currentHealth, maxHealth, healthRecoveryPerSecond * Time.fixedDeltaTime);
    }

    public void ApplyFireDamage(float damageAmount)
    {
        if (!isActiveAndEnabled || isSafe || liftable.IsLifted || currentState == VictimState.Downed ||
            currentState == VictimState.Dead || damageAmount <= 0f) return;
        currentHealth = Mathf.Max(0f, currentHealth - damageAmount);
        if (currentHealth > 0f) return;
        if (animator) ShowState(VictimState.Downed);
        else DieFromFire();
    }

    private void DieFromFire()
    {
        liftable.enabled = false;
        ShowState(VictimState.Dead);
    }

    public void SetSafeZone(SafeZoneTrigger zone, bool inside)
    {
        if (currentState == VictimState.Dead) return;
        if (!animator)
        {
            isSafe = inside;
            return;
        }
        if (inside) safeZones.Add(zone);
        else safeZones.Remove(zone);
    }

    private void OnDrawGizmosSelected()
    {
        var center = Application.isPlaying ? home : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, new Vector3(radiusX * 2f, .05f, radiusZ * 2f));
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, panicRadius);
    }
}
