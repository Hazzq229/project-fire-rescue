using UnityEngine;

public class SafeZoneTrigger : MonoBehaviour
{
    [SerializeField] private string victimTag = "Victim";

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(victimTag))
        {
            GameManager.Instance.SetVictimSafe(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(victimTag))
        {
            GameManager.Instance.SetVictimSafe(false);
        }
    }
}