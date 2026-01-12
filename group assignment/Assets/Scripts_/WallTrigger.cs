using UnityEngine;

public class TutorialTriggerRememberFreeze : MonoBehaviour
{
    public TutorialManager tutorial;

    [Tooltip("If true, disables this trigger after firing once.")]
    public bool oneShot = true;

    void Awake()
    {
        if (!tutorial) tutorial = FindFirstObjectByType<TutorialManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (!tutorial) return;

        tutorial.ShowFreezeReminder();

        if (oneShot)
            gameObject.SetActive(false);
    }
}
