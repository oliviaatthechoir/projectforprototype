using UnityEngine;

public class TutorialTriggerEndDone : MonoBehaviour
{
    public TutorialManager tutorial;
    public bool oneShot = true;

    void Awake()
    {
        if (!tutorial) tutorial = FindFirstObjectByType<TutorialManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (!tutorial) return;

        tutorial.ShowTutorialDone();

        if (oneShot)
            gameObject.SetActive(false);
    }
}
