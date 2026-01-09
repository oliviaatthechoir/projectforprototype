using System.Collections;
using UnityEngine;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    public enum Step
    {
        SelectCube,
        GrabCube,
        LiftAndDrop,
        FreezeCube,
        BuildStairs,
        PullBigBox,
        TutorialDone
    }

    [Header("UI")]
    public TextMeshProUGUI tutorialText;

    [Header("Timing")]
    [Tooltip("Delay before switching to the next tutorial text.")]
    public float textDelay = 0.6f;

    [Header("References")]
    public GrabTool grabTool;

    private Step currentStep = Step.SelectCube;
    private Coroutine stepRoutine;
    private bool isAdvancing;

    void Awake()
    {
        if (!grabTool) grabTool = FindFirstObjectByType<GrabTool>();
    }

    void OnEnable()
    {
        HookEvents(true);
    }

    void OnDisable()
    {
        HookEvents(false);
    }

    void Start()
    {
        ShowCurrentStepText();
    }

    // ---------------- Event Hookup ----------------

    void HookEvents(bool hook)
    {
        if (!grabTool) return;

        if (hook)
        {
            grabTool.BoxSelected += OnBoxSelected;
            grabTool.GrabStarted += OnGrabStarted;
            grabTool.DroppedNormally += OnDroppedNormally;
            grabTool.FrozenOnRelease += OnFrozenOnRelease;
        }
        else
        {
            grabTool.BoxSelected -= OnBoxSelected;
            grabTool.GrabStarted -= OnGrabStarted;
            grabTool.DroppedNormally -= OnDroppedNormally;
            grabTool.FrozenOnRelease -= OnFrozenOnRelease;
        }
    }

    // ---------------- Tutorial Progress (Event Driven) ----------------

    void OnBoxSelected(SelectableBox box)
    {
        if (currentStep != Step.SelectCube) return;
        AdvanceTo(Step.GrabCube);
    }

    void OnGrabStarted()
    {
        if (currentStep != Step.GrabCube) return;
        AdvanceTo(Step.LiftAndDrop);
    }

    void OnDroppedNormally()
    {
        if (currentStep != Step.LiftAndDrop) return;
        AdvanceTo(Step.FreezeCube);
    }

    void OnFrozenOnRelease()
    {
        if (currentStep != Step.FreezeCube) return;
        AdvanceTo(Step.BuildStairs);
    }

    // ---------------- Trigger calls from your WallTrigger / EndTrigger ----------------

    // Call this from your wall-top trigger when the player reaches the top.
    public void OnReachedWallTop()
    {
        if (currentStep != Step.BuildStairs) return;
        AdvanceTo(Step.PullBigBox);
    }

    // Call this from your end/gap trigger when the player crosses the gap / finishes.
    public void OnCrossedGapEnd()
    {
        if (currentStep != Step.PullBigBox) return;
        AdvanceTo(Step.TutorialDone);
    }

    // ---------------- Step Flow ----------------

    void AdvanceTo(Step next)
    {
        if (isAdvancing) return;

        // Prevent re-advancing to the same step or going backwards
        if (next == currentStep) return;

        if (stepRoutine != null) StopCoroutine(stepRoutine);
        stepRoutine = StartCoroutine(AdvanceWithDelay(next));
    }

    IEnumerator AdvanceWithDelay(Step next)
    {
        isAdvancing = true;

        if (textDelay > 0f)
            yield return new WaitForSeconds(textDelay);

        currentStep = next;
        ShowCurrentStepText();

        isAdvancing = false;
        stepRoutine = null;
    }

    void ShowCurrentStepText()
    {
        if (!tutorialText) return;

        switch (currentStep)
        {
            case Step.SelectCube:
                tutorialText.text = "Try selecting the cube with LEFT CLICK";
                break;

            case Step.GrabCube:
                tutorialText.text = "Nice! Now try moving the box by holding RIGHT CLICK";
                break;

            case Step.LiftAndDrop:
                tutorialText.text = "Try movingthe box into the air and RELEASE right click";
                break;

            case Step.FreezeCube:
                tutorialText.text = "Oh The box fell!\nNow try again but press F before releasing RIGHT CLICK to FREEZE it";
                break;

            case Step.BuildStairs:
                tutorialText.text = "Great! Now try to build a staircase up this wall using the boxes";
                break;

            case Step.PullBigBox:
                tutorialText.text = "Select the BIG box and drag it toward you using the SCROLL WHEEL";
                break;

            case Step.TutorialDone:
                tutorialText.text = "Great job — tutorial done!";
                break;
        }
    }
}
