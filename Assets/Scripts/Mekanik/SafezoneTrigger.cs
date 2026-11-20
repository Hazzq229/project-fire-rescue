using UnityEngine;

public class SafeZoneTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out VictimAI victim))
            victim.SetSafeZone(this, true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out VictimAI victim))
            victim.SetSafeZone(this, false);
    }
}
