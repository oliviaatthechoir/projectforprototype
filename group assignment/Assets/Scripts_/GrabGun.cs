using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GrabTool : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public Transform grabPoint;

    [Header("Selection / Grab Range (same)")]
    public int maxSmallSelections = 3;
    public float interactRange = 25f;          // ONE value for both select + grab start
    public LayerMask selectableMask;

    [Header("Hold Distance")]
    public float minGrabDistance = 2f;
    public float maxGrabDistance = 35f;

    [Tooltip("Higher = stronger push/pull. (Try 0.01 to 0.08 depending on mouse)")]
    public float scrollSensitivity = 0.02f;

    [Tooltip("Optional extra multiplier for global strength.")]
    public float scrollDistanceScale = 1.0f;

    [Header("Hold Movement")]
    public float moveSpeed = 20f;

    [Header("Collision While Holding (anti-clip)")]
    public LayerMask holdCollisionMask = ~0;
    public float holdSkin = 0.03f;

    [Header("Freeze Toggle")]
    public Key freezeKey = Key.F;

    [Header("Grab Rules")]
    [Tooltip("If true, you must be aiming at one of the selected boxes to start grabbing.")]
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

    // Public state for tutorial queries
    public bool IsGrabbing => _isGrabbing;
    public bool HasAnySelected => _selected.Count > 0;
    public bool FreezeMode => _freezeMode;

    // Simple one-shot flags (tutorial-friendly)
    public bool DroppedUnfrozenThisFrame { get; private set; }
    public bool FrozeThisFrame { get; private set; }
    public bool SelectedSomethingThisFrame { get; private set; }
    public bool GrabStartedThisFrame { get; private set; }

    private readonly List<SelectableBox> _selected = new();
    private readonly Dictionary<SelectableBox, Vector3> _localOffsets = new();

    private bool _isGrabbing;
    private bool _freezeMode;
    private float _grabDistance;

    void Start()
    {
        if (!cam) cam = Camera.main;

        float startDist = Vector3.Distance(cam.transform.position, grabPoint.position);
        _grabDistance = Mathf.Clamp(startDist, minGrabDistance, maxGrabDistance);
    }

    void Update()
    {
        if (Mouse.current == null) return;

        // reset one-shot flags
        DroppedUnfrozenThisFrame = false;
        FrozeThisFrame = false;
        SelectedSomethingThisFrame = false;
        GrabStartedThisFrame = false;

        // Freeze toggle
        if (Keyboard.current != null && Keyboard.current[freezeKey].wasPressedThisFrame)
        {
            _freezeMode = !_freezeMode;
            FreezeModeChanged?.Invoke(_freezeMode);
        }

        HandleSelect();
        HandleGrab();
    }

    // ---------------- Selection ----------------

    void HandleSelect()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        if (!TryRaycastBox(interactRange, out var box))
            return;

        // Toggle deselect
        if (_selected.Contains(box))
        {
            Deselect(box);
            return;
        }

        // Clicking frozen box: unfreeze + select, and freeze mode resets off
        if (box.IsFrozen)
        {
            _freezeMode = false;
            FreezeModeChanged?.Invoke(_freezeMode);

            box.UnfreezeToDynamic();
        }

        // Heavy: only one, clear others
        if (box.boxType == BoxType.Heavy)
        {
            ClearSelectionVisualsOnly();
            _selected.Clear();

            _selected.Add(box);
            box.SetSelectedVisual();
            BoxSelected?.Invoke(box);
            SelectedSomethingThisFrame = true;
            return;
        }

        // If heavy selected, clear it before selecting smalls
        if (HasHeavySelected())
        {
            ClearSelectionVisualsOnly();
            _selected.Clear();
        }

        // Max small selection
        if (CountSmallSelected() >= maxSmallSelections) return;

        _selected.Add(box);
        box.SetSelectedVisual();
        BoxSelected?.Invoke(box);
        SelectedSomethingThisFrame = true;
    }

    // ---------------- Grabbing ----------------

    void HandleGrab()
    {
        // Start grab
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (_selected.Count == 0) return;

            if (requireAimAtSelectedToGrab)
            {
                if (!TryRaycastBox(interactRange, out var aimedBox)) return;
                if (!_selected.Contains(aimedBox)) return;
            }

            _isGrabbing = true;
            GrabStarted?.Invoke();
            GrabStartedThisFrame = true;

            _localOffsets.Clear();

            foreach (var b in _selected)
            {
                // Heavy base Y for lift clamp
                if (b.boxType == BoxType.Heavy)
                    b.heavyBaseY = b.transform.position.y;

                b.BeginHold();
                b.SetSelectedVisual();

                // capture offsets in grabPoint space (formation hold)
                Vector3 offsetWorld = b.transform.position - grabPoint.position;
                Vector3 offsetLocal = Quaternion.Inverse(grabPoint.rotation) * offsetWorld;
                _localOffsets[b] = offsetLocal;
            }
        }

        if (!_isGrabbing) return;

        // While holding, preview freeze color (so you KNOW before releasing)
        foreach (var b in _selected)
        {
            if (_freezeMode) b.SetFrozenVisual();
            else b.SetSelectedVisual();
        }

        // Scroll push/pull (STRONG)
        float scrollRaw = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollRaw) > 0.01f)
        {
            ScrolledWhileHolding?.Invoke(scrollRaw);

            // Normalize typical 120-step wheels to ~1
            float scrollNorm = scrollRaw / 120f;

            // Make it stronger the farther away you are
            float dist01 = Mathf.InverseLerp(minGrabDistance, maxGrabDistance, _grabDistance);
            float distanceFactor = Mathf.Lerp(1f, 8f, dist01);

            float delta = scrollNorm * scrollSensitivity * scrollDistanceScale * distanceFactor;
            _grabDistance = Mathf.Clamp(_grabDistance + delta, minGrabDistance, maxGrabDistance);
        }

        // keep grab point in front of camera
        grabPoint.position = cam.transform.position + cam.transform.forward * _grabDistance;
        grabPoint.rotation = cam.transform.rotation;

        // Move held boxes with sweep-test anti-clip
        foreach (var b in _selected)
        {
            if (!_localOffsets.TryGetValue(b, out var offLocal))
                continue;

            Vector3 targetPos = grabPoint.position + grabPoint.rotation * offLocal;

            // Heavy: clamp lift
            if (b.boxType == BoxType.Heavy)
            {
                float maxY = b.heavyBaseY + b.heavyMaxLift;
                targetPos.y = Mathf.Min(targetPos.y, maxY);
            }

            Vector3 current = b.rb.position;

            // Lerp toward target
            Vector3 desired = Vector3.Lerp(current, targetPos, Time.deltaTime * moveSpeed);

            Vector3 deltaVec = desired - current;
            float dist = deltaVec.magnitude;

            if (dist > 0.0001f)
            {
                Vector3 dir = deltaVec / dist;

                // Sweep to prevent clipping into environment
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
            GrabReleased?.Invoke();

            bool froze = _freezeMode;

            foreach (var b in _selected)
            {
                if (froze) b.FreezeHard();
                else { b.DropDynamic(); b.FadeToNormal(); }
            }

            if (froze)
            {
                FrozenOnRelease?.Invoke();
                FrozeThisFrame = true;
            }
            else
            {
                DroppedNormally?.Invoke();
                DroppedUnfrozenThisFrame = true;
            }

            _isGrabbing = false;
            _localOffsets.Clear();
            _selected.Clear();

            // IMPORTANT: freeze should always start OFF again after being used
            if (_freezeMode)
            {
                _freezeMode = false;
                FreezeModeChanged?.Invoke(_freezeMode);
            }
        }
    }

    // ---------------- Helpers ----------------

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
        _selected.Remove(box);

        if (box.IsFrozen) box.SetFrozenVisual();
        else box.SetNormalInstant();

        BoxDeselected?.Invoke(box);
    }

    void ClearSelectionVisualsOnly()
    {
        foreach (var b in _selected)
        {
            if (b.IsFrozen) b.SetFrozenVisual();
            else b.SetNormalInstant();
        }
    }

    bool HasHeavySelected() => _selected.Count == 1 && _selected[0].boxType == BoxType.Heavy;

    int CountSmallSelected()
    {
        int c = 0;
        foreach (var s in _selected)
            if (s.boxType == BoxType.Small) c++;
        return c;
    }
}
