using UnityEngine;

public class TutorialTriggerGapEnd : MonoBehaviour
{
    public TutorialManager tutorial;

    void Awake()
    {
        if (!tutorial) tutorial = FindFirstObjectByType<TutorialManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        tutorial.OnCrossedGapEnd();
        gameObject.SetActive(false);
    }
}
