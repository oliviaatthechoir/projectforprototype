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
    public float selectRange = 12f;            
    public float grabStartRange = 12f;         
    public LayerMask selectableMask;

    [Header("Hold Distance")]
    public float minGrabDistance = 1.5f;
    public float maxGrabDistance = 12f;        
    public float scrollSensitivity = 4.0f;     

    [Header("Hold Movement")]
    public float moveSpeed = 20f;

    [Header("Collision While Holding")]
    public LayerMask holdCollisionMask = ~0;
    public float holdSkin = 0.03f;

    [Header("Freeze Toggle")]
    public Key freezeKey = Key.F;

    [Header("Grab Rules")]
    public bool requireAimAtSelectedToGrab = true;

    private readonly List<SelectableBox> selected = new();
    private readonly Dictionary<SelectableBox, Vector3> localOffsets = new();

    private bool isGrabbing;
    private bool freezeMode; // TOGGLE
    private float grabDistance;

    void Start()
    {
        if (!cam) cam = Camera.main;
        grabDistance = Mathf.Clamp(Vector3.Distance(cam.transform.position, grabPoint.position), minGrabDistance, maxGrabDistance);
    }

    void Update()
    {
        if (Mouse.current == null) return;

        // Toggle freeze
        if (Keyboard.current != null && Keyboard.current[freezeKey].wasPressedThisFrame)
            freezeMode = !freezeMode;

        HandleSelect();
        HandleGrab();
    }

    // ---------------- Selection ----------------

    void HandleSelect()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        if (!TryRaycastBox(selectRange, out var box))
            return;

        // Clicking a frozen box = unfreeze + select
        if (box.IsFrozen)
        {
            freezeMode = false;

            if (box.boxType == BoxType.Heavy)
            {
                ClearSelectionVisualsOnly();
                selected.Clear();

                box.UnfreezeToDynamic();
                selected.Add(box);
                box.SetSelectedVisual();
                return;
            }

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

        // Heavy: only one, clear others
        if (box.boxType == BoxType.Heavy)
        {
            ClearSelectionVisualsOnly();
            selected.Clear();

            selected.Add(box);
            box.SetSelectedVisual();
            return;
        }

        // Selecting small while heavy selected clears heavy
        if (HasHeavySelected())
        {
            ClearSelectionVisualsOnly();
            selected.Clear();
        }

        // Max small selections
        if (CountSmallSelected() >= maxSmallSelections) return;

        selected.Add(box);
        box.SetSelectedVisual();
    }

    // ---------------- Grabbing ----------------

    void HandleGrab()
    {
        // Start grab
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (selected.Count == 0) return;

            if (requireAimAtSelectedToGrab)
            {
                if (!TryRaycastBox(grabStartRange, out var aimedBox)) return;
                if (!selected.Contains(aimedBox)) return;
            }

            isGrabbing = true;
            localOffsets.Clear();

            foreach (var b in selected)
            {
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

        // Preview freeze while holding
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

        
        foreach (var b in selected)
        {
            if (!localOffsets.TryGetValue(b, out var offLocal))
                continue;

            Vector3 targetPos = grabPoint.position + grabPoint.rotation * offLocal;

            // Heavy: clamp lift
            if (b.boxType == BoxType.Heavy)
            {
                float maxY = b.heavyBaseY + b.heavyMaxLift;
                targetPos.y = Mathf.Min(targetPos.y, maxY);
            }

            Vector3 desired = Vector3.Lerp(b.rb.position, targetPos, Time.deltaTime * moveSpeed);

            Vector3 current = b.rb.position;
            Vector3 delta = desired - current;
            float dist = delta.magnitude;

            if (dist > 0.0001f)
            {
                Vector3 dir = delta / dist;

                // prevent clipping through environment
                if (b.rb.SweepTest(dir, out RaycastHit hit, dist, QueryTriggerInteraction.Ignore))
                {
                    int hitLayer = hit.collider.gameObject.layer;
                    if (((1 << hitLayer) & holdCollisionMask.value) != 0)
                    {
                        float safeDist = Mathf.Max(0f, hit.distance - holdSkin);
                        desired = current + dir * safeDist;
                    }
                }

                b.rb.MovePosition(desired);
            }
        }

        // Release grab
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

            // reset selection 
            isGrabbing = false;
            localOffsets.Clear();
            selected.Clear();

            // Freeze start OFF 
            if (freezeMode) freezeMode = false;
        }
    }

    
    bool TryRaycastBox(float range, out SelectableBox box)
    {
        box = null;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, range, selectableMask))
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
