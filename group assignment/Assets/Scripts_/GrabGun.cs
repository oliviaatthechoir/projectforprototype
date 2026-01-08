using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GrabTool : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public Transform grabPoint;

    [Header("Selection")]
    public int maxSmallSelections = 3;
    public float selectRange = 6f;
    public LayerMask selectableMask;

    [Header("Grab Movement")]
    public float moveSpeed = 20f;
    public float scrollSensitivity = 1.5f;
    public float minGrabDistance = 1.0f;
    public float maxGrabDistance = 6.0f;

    [Header("Freeze Toggle")]
    public Key freezeKey = Key.F;

    [Header("Grab rules")]
    public bool requireAimAtSelectedToGrab = true;

    private readonly List<SelectableBox> selected = new();
    private readonly Dictionary<SelectableBox, Vector3> localOffsets = new();

    private bool isGrabbing;
    private bool freezeMode;     // TOGGLE
    private float grabDistance;

    void Start()
    {
        if (!cam) cam = Camera.main;
        grabDistance = Vector3.Distance(cam.transform.position, grabPoint.position);
    }

    void Update()
    {
        if (Mouse.current == null) return;

        // Toggle freeze mode
        if (Keyboard.current != null && Keyboard.current[freezeKey].wasPressedThisFrame)
            freezeMode = !freezeMode;

        HandleSelect();
        HandleGrab();
    }

    void HandleSelect()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (!TryRaycastBox(out var box)) return;

        // Clicking a frozen box should unfreeze + select it again
        if (box.IsFrozen)
        {
            // Heavy rule: heavy selection clears others
            if (box.boxType == BoxType.Heavy)
            {
                ClearSelectionVisualsOnly();
                selected.Clear();

                box.UnfreezeToDynamic();
                selected.Add(box);
                box.SetSelectedVisual();
                return;
            }

            // If heavy currently selected, clear it first
            if (HasHeavySelected())
            {
                ClearSelectionVisualsOnly();
                selected.Clear();
            }

            if (CountSmallSelected() >= maxSmallSelections) return;

            box.UnfreezeToDynamic();
            selected.Add(box);
            box.SetSelectedVisual();
            return;
        }

        // Toggle deselect
        if (selected.Contains(box))
        {
            Deselect(box);
            return;
        }

        // Heavy selection clears others
        if (box.boxType == BoxType.Heavy)
        {
            ClearSelectionVisualsOnly();
            selected.Clear();

            selected.Add(box);
            box.SetSelectedVisual();
            return;
        }

        // Small selection clears heavy if needed
        if (HasHeavySelected())
        {
            ClearSelectionVisualsOnly();
            selected.Clear();
        }

        if (CountSmallSelected() >= maxSmallSelections) return;

        selected.Add(box);
        box.SetSelectedVisual();
    }

    void HandleGrab()
    {
        // Start grab only if something is selected (and optionally you're aiming at a selected box)
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (selected.Count == 0) return;

            if (requireAimAtSelectedToGrab)
            {
                if (!TryRaycastBox(out var aimedBox)) return;
                if (!selected.Contains(aimedBox)) return;
            }

            isGrabbing = true;
            localOffsets.Clear();

            foreach (var b in selected)
            {
                // refresh baseY for heavy so "lift clamp" is from current position
                if (b.boxType == BoxType.Heavy)
                    b.heavyBaseY = b.transform.position.y;

                b.BeginHold();
                b.SetSelectedVisual();

                Vector3 offsetWorld = b.transform.position - grabPoint.position;
                Vector3 offsetLocal = Quaternion.Inverse(grabPoint.rotation) * offsetWorld;
                localOffsets[b] = offsetLocal;
            }
        }

        if (!isGrabbing) return;

        // PREVIEW: while holding, show whether freeze mode is armed
        foreach (var b in selected)
        {
            if (freezeMode) b.SetFrozenVisual();
            else b.SetSelectedVisual();
        }

        // Scroll push/pull
        float scrollRaw = Mouse.current.scroll.ReadValue().y;
        float scroll = Mathf.Clamp(scrollRaw / 120f, -1f, 1f);

        if (Mathf.Abs(scroll) > 0.001f)
            grabDistance = Mathf.Clamp(grabDistance + scroll * scrollSensitivity, minGrabDistance, maxGrabDistance);

        grabPoint.position = cam.transform.position + cam.transform.forward * grabDistance;
        grabPoint.rotation = cam.transform.rotation;

        // Move held boxes (kinematic)
        foreach (var b in selected)
        {
            if (!localOffsets.TryGetValue(b, out var offLocal)) continue;

            Vector3 targetPos = grabPoint.position + grabPoint.rotation * offLocal;

            if (b.boxType == BoxType.Heavy)
            {
                float maxY = b.heavyBaseY + b.heavyMaxLift;
                targetPos.y = Mathf.Min(targetPos.y, maxY);
            }

            Vector3 newPos = Vector3.Lerp(b.transform.position, targetPos, Time.deltaTime * moveSpeed);
            b.rb.MovePosition(newPos);
        }

        // Release
        if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            foreach (var b in selected)
            {
                if (freezeMode)
                {
                    b.FreezeHard();
                }
                else
                {
                    b.DropDynamic();
                    b.FadeToNormal();
                }
            }

            // CRITICAL RESET so old selections cannot "wake up" later
            isGrabbing = false;
            localOffsets.Clear();
            selected.Clear();
        }
    }

    bool TryRaycastBox(out SelectableBox box)
    {
        box = null;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, selectRange, selectableMask))
            return false;

        box = hit.collider.GetComponentInParent<SelectableBox>();
        return box != null;
    }

    void Deselect(SelectableBox box)
    {
        selected.Remove(box);
        if (box.IsFrozen) box.SetFrozenVisual();
        else box.SetNormalInstant();
    }

    void ClearSelectionVisualsOnly()
    {
        foreach (var b in selected)
        {
            if (b.IsFrozen) b.SetFrozenVisual();
            else b.SetNormalInstant();
        }
    }

    bool HasHeavySelected()
    {
        return selected.Count == 1 && selected[0].boxType == BoxType.Heavy;
    }

    int CountSmallSelected()
    {
        int count = 0;
        foreach (var s in selected)
            if (s.boxType == BoxType.Small) count++;
        return count;
    }
}
