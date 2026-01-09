using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GrabTool : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public Transform grabPoint;

    [Header("Selection / Grab Range")]
    public int maxSmallSelections = 3;

    [Tooltip("How far you can SELECT and START grabbing (same range).")]
    public float interactRange = 40f;

    [Tooltip("Sphere radius for aim checks (helps grabbing at long range).")]
    public float grabAimRadius = 0.25f;

    public LayerMask selectableMask;

    [Header("Hold Distance")]
    public float minGrabDistance = 2f;
    public float maxGrabDistance = 60f;

    [Header("Scroll Feel")]
    [Tooltip("Higher = faster push/pull. Try 1.0–3.0")]
    public float scrollStrength = 2.0f;

    [Tooltip("If scroll direction feels backwards, enable this.")]
    public bool invertScroll = false;

    [Header("Hold Movement")]
    public float moveSpeed = 20f;

    [Header("Collision While Holding (anti-clip)")]
    public LayerMask holdCollisionMask = ~0;
    public float holdSkin = 0.03f;

    [Header("Freeze Toggle")]
    public Key freezeKey = Key.F;

    [Header("Grab Rules")]
    public bool requireAimAtSelectedToGrab = true;

    // ---------------- EVENTS for Tutorial/UI ----------------
    public event Action<SelectableBox> BoxSelected;
    public event Action<SelectableBox> BoxDeselected;
    public event Action GrabStarted;
    public event Action GrabReleased;
    public event Action DroppedNormally;
    public event Action FrozenOnRelease;
    public event Action<bool> FreezeModeChanged;
    public event Action<float> ScrolledWhileHolding;
    // --------------------------------------------------------

    private readonly List<SelectableBox> selected = new();
    private readonly Dictionary<SelectableBox, Vector3> localOffsets = new();

    private bool isGrabbing;
    private bool freezeMode;
    private float grabDistance;

    // These exist (won’t break anything even if you no longer use them)
    public bool SelectedSomethingThisFrame { get; private set; }
    public bool GrabStartedThisFrame { get; private set; }
    public bool DroppedUnfrozenThisFrame { get; private set; }
    public bool FrozeThisFrame { get; private set; }

    public bool IsGrabbingNow => isGrabbing;
    public bool HasAnySelected => selected.Count > 0;

    private const float WheelStep = 120f; // many mice report +/-120 per notch

    void Start()
    {
        if (!cam) cam = Camera.main;

        if (!grabPoint)
        {
            var go = new GameObject("GrabPoint");
            grabPoint = go.transform;
        }

        grabDistance = Mathf.Clamp(6f, minGrabDistance, maxGrabDistance);
    }

    void Update()
    {
        if (Mouse.current == null) return;

        // Freeze toggle (F)
        if (Keyboard.current != null && Keyboard.current[freezeKey].wasPressedThisFrame)
        {
            freezeMode = !freezeMode;
            FreezeModeChanged?.Invoke(freezeMode);
        }

        HandleSelect();
        HandleGrab();
    }

    void LateUpdate()
    {
        SelectedSomethingThisFrame = false;
        GrabStartedThisFrame = false;
        DroppedUnfrozenThisFrame = false;
        FrozeThisFrame = false;
    }

    // ---------------- Selection ----------------

    void HandleSelect()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        if (!TryRaycastBox(interactRange, out var box))
            return;

        // Toggle deselect
        if (selected.Contains(box))
        {
            Deselect(box);
            return;
        }

        // Clicking frozen box: unfreeze + select, and freeze mode resets off
        if (box.IsFrozen)
        {
            freezeMode = false;
            FreezeModeChanged?.Invoke(freezeMode);
            box.UnfreezeToDynamic();
        }

        // Heavy: only one, clear others
        if (box.boxType == BoxType.Heavy)
        {
            ClearSelectionVisualsOnly();
            selected.Clear();

            selected.Add(box);
            box.SetSelectedVisual();

            SelectedSomethingThisFrame = true;
            BoxSelected?.Invoke(box);
            return;
        }

        // If heavy selected, clear it before selecting smalls
        if (HasHeavySelected())
        {
            ClearSelectionVisualsOnly();
            selected.Clear();
        }

        if (CountSmallSelected() >= maxSmallSelections) return;

        selected.Add(box);
        box.SetSelectedVisual();

        SelectedSomethingThisFrame = true;
        BoxSelected?.Invoke(box);
    }

    // ---------------- Grabbing ----------------

    void HandleGrab()
    {
        // Start grab
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (selected.Count == 0) return;

            SelectableBox aimedBox = null;

            if (requireAimAtSelectedToGrab)
            {
                if (!TrySpherecastBox(interactRange, grabAimRadius, out aimedBox)) return;
                if (!selected.Contains(aimedBox)) return;

                // Start distance based on what you aimed at (feels good at range)
                grabDistance = Mathf.Clamp(
                    Vector3.Distance(cam.transform.position, aimedBox.transform.position),
                    minGrabDistance, maxGrabDistance
                );
            }

            isGrabbing = true;
            GrabStartedThisFrame = true;
            GrabStarted?.Invoke();

            localOffsets.Clear();

            // Set grab point immediately
            grabPoint.position = cam.transform.position + cam.transform.forward * grabDistance;
            grabPoint.rotation = cam.transform.rotation;

            foreach (var b in selected)
            {
                if (b.boxType == BoxType.Heavy)
                    b.heavyBaseY = b.transform.position.y;

                b.BeginHold();
                b.SetSelectedVisual();

                // Store offset in grabPoint local space…
                Vector3 offsetWorld = b.transform.position - grabPoint.position;
                Vector3 offsetLocal = Quaternion.Inverse(grabPoint.rotation) * offsetWorld;

                // ✅ KEY FIX: DO NOT preserve depth offset.
                // Depth is controlled only by grabDistance, so it will not "spring back".
                offsetLocal.z = 0f;

                localOffsets[b] = offsetLocal;
            }
        }

        if (!isGrabbing) return;

        // Visual preview while holding
        foreach (var b in selected)
            if (freezeMode) b.SetFrozenVisual();
            else b.SetSelectedVisual();

        // Scroll push/pull (true magnet, both directions)
        float scrollRaw = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollRaw) > 0.01f)
        {
            ScrolledWhileHolding?.Invoke(scrollRaw);

            float notches = scrollRaw / WheelStep;
            if (invertScroll) notches *= -1f;

            // Stronger when farther away (GMod-ish)
            float dist01 = Mathf.InverseLerp(minGrabDistance, maxGrabDistance, grabDistance);
            float distanceFactor = Mathf.Lerp(1f, 8f, dist01);

            float delta = notches * scrollStrength * distanceFactor;

            // ✅ delta can be positive OR negative -> push OR pull
            grabDistance = Mathf.Clamp(grabDistance + delta, minGrabDistance, maxGrabDistance);
        }

        // Keep grab point in front of camera
        grabPoint.position = cam.transform.position + cam.transform.forward * grabDistance;
        grabPoint.rotation = cam.transform.rotation;

        // Move held boxes with sweep-test anti-clip
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

            Vector3 current = b.rb.position;
            Vector3 desired = Vector3.Lerp(current, targetPos, Time.deltaTime * moveSpeed);

            Vector3 deltaVec = desired - current;
            float dist = deltaVec.magnitude;

            if (dist > 0.0001f)
            {
                Vector3 dir = deltaVec / dist;

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

        // Release
        if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            GrabReleased?.Invoke();

            bool froze = freezeMode;

            foreach (var b in selected)
            {
                if (froze) b.FreezeHard();
                else { b.DropDynamic(); b.FadeToNormal(); }
            }

            if (froze)
            {
                FrozeThisFrame = true;
                FrozenOnRelease?.Invoke();
            }
            else
            {
                DroppedUnfrozenThisFrame = true;
                DroppedNormally?.Invoke();
            }

            isGrabbing = false;
            localOffsets.Clear();
            selected.Clear();

            // Freeze resets OFF after use
            if (freezeMode)
            {
                freezeMode = false;
                FreezeModeChanged?.Invoke(freezeMode);
            }
        }
    }

    // ---------------- Ray helpers ----------------

    bool TryRaycastBox(float range, out SelectableBox box)
    {
        box = null;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                out var hit, range, selectableMask, QueryTriggerInteraction.Ignore))
            return false;

        box = hit.collider.GetComponentInParent<SelectableBox>();
        return box != null;
    }

    bool TrySpherecastBox(float range, float radius, out SelectableBox box)
    {
        box = null;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.SphereCast(ray, radius, out var hit, range, selectableMask, QueryTriggerInteraction.Ignore))
            return false;

        box = hit.collider.GetComponentInParent<SelectableBox>();
        return box != null;
    }

    // ---------------- Selection helpers ----------------

    void Deselect(SelectableBox box)
    {
        selected.Remove(box);
        if (box.IsFrozen) box.SetFrozenVisual();
        else box.SetNormalInstant();

        BoxDeselected?.Invoke(box);
    }

    void ClearSelectionVisualsOnly()
    {
        foreach (var b in selected)
            if (b.IsFrozen) b.SetFrozenVisual();
            else b.SetNormalInstant();
    }

    bool HasHeavySelected() => selected.Count == 1 && selected[0].boxType == BoxType.Heavy;

    int CountSmallSelected()
    {
        int c = 0;
        foreach (var s in selected)
            if (s.boxType == BoxType.Small) c++;
        return c;
    }
}
