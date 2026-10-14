using UnityEngine;

public class SafeZoneTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out VictimAI victim))
        {
            victim.isSafe = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out VictimAI victim))
        {
            victim.isSafe = false;
        }
    }
}