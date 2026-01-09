using UnityEngine;

public class TutorialTriggerWallTop : MonoBehaviour
{
    public TutorialManager tutorial;

    void Awake()
    {
        if (!tutorial) tutorial = FindFirstObjectByType<TutorialManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        tutorial.OnReachedWallTop();

        // optional: disable so it only happens once
        gameObject.SetActive(false);
    }
}
