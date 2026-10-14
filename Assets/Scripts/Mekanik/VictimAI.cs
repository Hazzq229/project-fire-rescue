using UnityEngine;
using HInteractions;

[RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody), typeof(Liftable))]
public class VictimAI : MonoBehaviour
{
    public enum VictimState { Calm, Panic, Cower, Dead }
    public VictimState currentState = VictimState.Calm;

    [Header("Panic Movement Settings")]
    public float panicSpeed = 2f;
    public float radiusX = 3f;
    public float radiusZ = 3f;

    [Header("Fire Damage Prototype")]
    public float maxHealth = 100f;
    private float currentHealth;
    [HideInInspector] public bool isSafe = false;

    private Vector3 startPosition;
    private float currentAngle;

    private Rigidbody rb;
    private Liftable liftable;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        liftable = GetComponent<Liftable>();

        startPosition = transform.position;
        currentHealth = maxHealth;
    }

    void Update()
    {
        CheckInteractionState();

        switch (currentState)
        {
            case VictimState.Calm:
                // TODO: Trigger animasi Calm
                break;
            case VictimState.Panic:
                RunInEllipse();
                break;
            case VictimState.Cower:
                // TODO: Trigger animasi Cower
                break;
            case VictimState.Dead:
                // TODO: Logika state mati
                break;
        }
    }

    private void CheckInteractionState()
    {
        if (currentState == VictimState.Dead) return;

        // Mengecek properti IsLifted dari Liftable.cs
        if (liftable.IsLifted)
        {
            currentState = VictimState.Cower;
        }
        else if (currentState == VictimState.Cower)
        {
            // Mengembalikan ke Calm jika dilepas. Bisa disesuaikan jika ingin tetap Cower atau berubah Panik
            currentState = VictimState.Calm;
        }
    }

    private void RunInEllipse()
    {
        currentAngle += panicSpeed * Time.deltaTime;

        float x = startPosition.x + Mathf.Cos(currentAngle) * radiusX;
        float z = startPosition.z + Mathf.Sin(currentAngle) * radiusZ;

        Vector3 targetPosition = new Vector3(x, transform.position.y, z);

        Vector3 moveDirection = (targetPosition - transform.position).normalized;
        if (moveDirection != Vector3.zero)
        {
            transform.forward = moveDirection; // Karakter menghadap arah gerak
        }

        rb.MovePosition(targetPosition);
    }

    // Prototype Method untuk kerusakan akibat api
    public void ApplyFireDamage(float damageAmount)
    {
        if (currentState == VictimState.Dead) return;

        currentHealth -= damageAmount;

        if (currentHealth <= 0)
        {
            DieFromFire();
        }
    }

    private void DieFromFire()
    {
        currentState = VictimState.Dead;
        // TODO: Matikan pergerakan, disable collider, atau jalankan animasi mati
    }

    // Menggambar lintasan elips merah di Scene view
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 center = Application.isPlaying ? startPosition : transform.position;

        int segments = 24;
        Vector3 previousPoint = center + new Vector3(radiusX, 0, 0);

        for (int i = 1; i <= segments; i++)
        {
            float rad = Mathf.Deg2Rad * (i * 360f / segments);
            Vector3 currentPoint = center + new Vector3(Mathf.Cos(rad) * radiusX, 0, Mathf.Sin(rad) * radiusZ);
            Gizmos.DrawLine(previousPoint, currentPoint);
            previousPoint = currentPoint;
        }
    }
}