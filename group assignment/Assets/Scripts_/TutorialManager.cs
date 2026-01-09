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
    private bool waitingForDelay;

    void Start()
    {
        if (!grabTool) grabTool = FindFirstObjectByType<GrabTool>();
        ShowCurrentStepText();
    }

    void Update()
    {
        if (waitingForDelay) return;
        if (!grabTool || !tutorialText) return;

        switch (currentStep)
        {
            case Step.SelectCube:
                // Any selection
                if (grabTool.SelectedSomethingThisFrame || grabTool.HasAnySelected)
                    AdvanceTo(Step.GrabCube);
                break;

            case Step.GrabCube:
                // Started holding (right click)
                if (grabTool.GrabStartedThisFrame || grabTool.IsGrabbing)
                    AdvanceTo(Step.LiftAndDrop);
                break;

            case Step.LiftAndDrop:
                // Released WITHOUT freezing
                if (grabTool.DroppedUnfrozenThisFrame)
                    AdvanceTo(Step.FreezeCube);
                break;

            case Step.FreezeCube:
                // Released WITH freezing
                if (grabTool.FrozeThisFrame)
                    AdvanceTo(Step.BuildStairs);
                break;

            case Step.BuildStairs:
                // handled by WallTrigger -> OnReachedWallTop()
                break;

            case Step.PullBigBox:
                // handled by EndTrigger -> OnCrossedGapEnd()
                break;

            case Step.TutorialDone:
                break;
        }
    }

    // Trigger script calls this when player reaches top of wall
    public void OnReachedWallTop()
    {
        if (waitingForDelay) return;
        if (currentStep == Step.BuildStairs)
            AdvanceTo(Step.PullBigBox);
    }

    // Trigger script calls this when player crosses the gap / finishes
    public void OnCrossedGapEnd()
    {
        if (waitingForDelay) return;
        if (currentStep == Step.PullBigBox)
            AdvanceTo(Step.TutorialDone);
    }

    // ---------------- STEP FLOW ----------------

    void AdvanceTo(Step next)
    {
        if (waitingForDelay) return;
        StartCoroutine(AdvanceWithDelay(next));
    }

    IEnumerator AdvanceWithDelay(Step next)
    {
        waitingForDelay = true;

        yield return new WaitForSeconds(textDelay);

        currentStep = next;
        ShowCurrentStepText();

        waitingForDelay = false;
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
                tutorialText.text = "Move the block into the air and RELEASE right click";
                break;

            case Step.FreezeCube:
                tutorialText.text = "The block fell.\nNow press F before releasing RIGHT CLICK to FREEZE it";
                break;

            case Step.BuildStairs:
                tutorialText.text = "Great! Use that knowledge to build a staircase up this wall";
                break;

            case Step.PullBigBox:
                tutorialText.text = "Try selecting the BIG box and drag it toward you using the SCROLL WHEEL";
                break;

            case Step.TutorialDone:
                tutorialText.text = "Great job — tutorial done!";
                break;
        }
    }
}
