using System.Collections;
using UnityEngine;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI tutorialText;

    [Header("Timing")]
    [Tooltip("How long each message stays on screen.")]
    public float messageDuration = 4.0f;

    [Tooltip("Delay before a message appears (nice little pause).")]
    public float showDelay = 0.25f;

    [Header("Messages")]
    [TextArea(2, 6)]
    public string startMessage =
        "You’ve unlocked the gravity gun!.\nTry it out on the box in front of you.";

    [TextArea(2, 6)]
    public string freezeReminderMessage =
        "Remember: you can freeze boxes in the air with F.";

    [TextArea(2, 6)]
    public string tutorialDoneMessage =
        "Great job — tutorial done!";

    private Coroutine _messageRoutine;

    void Start()
    {
        // show a single starting message
        ShowMessage(startMessage);
    }

    // Call from triggers 
    public void ShowFreezeReminder()
    {
        ShowMessage(freezeReminderMessage);
    }

    public void ShowTutorialDone()
    {
        ShowMessage(tutorialDoneMessage);
    }

    // ---------------- Core UI ----------------

    public void ShowMessage(string msg)
    {
        if (!tutorialText) return;

        if (_messageRoutine != null)
            StopCoroutine(_messageRoutine);

        _messageRoutine = StartCoroutine(ShowMessageRoutine(msg));
    }

    IEnumerator ShowMessageRoutine(string msg)
    {
        // optional delay before it appears
        if (showDelay > 0f)
            yield return new WaitForSeconds(showDelay);

        tutorialText.gameObject.SetActive(true);
        tutorialText.text = msg;

        if (messageDuration > 0f)
            yield return new WaitForSeconds(messageDuration);

        tutorialText.text = "";
        tutorialText.gameObject.SetActive(false);

        _messageRoutine = null;
    }
}
