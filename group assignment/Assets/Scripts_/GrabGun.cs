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

    [Tooltip("Select + grab start range (same).")]
    public float interactRange = 35f;              // LONGER by default

    [Tooltip("Bigger = easier to start grab at distance (GMod-ish).")]
    public float grabAimRadius = 0.2f;             // forgiving aim

    public LayerMask selectableMask;

    [Header("Hold Distance")]
    public float minGrabDistance = 2f;
    public float maxGrabDistance = 60f;            // LONGER hold range

    [Tooltip("Scroll strength. Higher = stronger. Start with 0.002–0.01")]
    public float scrollSensitivity = 0.004f;       // STRONG

    [Tooltip("Extra multiplier.")]
    public float scrollDistanceScale = 1.0f;

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

    // Public state for tutorial
    public bool IsGrabbing => _isGrabbing;
    public bool HasAnySelected => _selected.Count > 0;
    public bool FreezeMode => _freezeMode;

    // One-frame tutorial flags
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

        // sensible start
        _grabDistance = Mathf.Clamp(6f, minGrabDistance, maxGrabDistance);

        if (grabPoint == null)
        {
            // safety: create a grab point if missing
            var go = new GameObject("GrabPoint");
            grabPoint = go.transform;
        }
    }

    void Update()
    {
        if (Mouse.current == null) return;

        // reset one-shot flags
        DroppedUnfrozenThisFrame = false;
        FrozeThisFrame = false;
        SelectedSomethingThisFrame = false;
        GrabStartedThisFrame = false;

        // Toggle freeze
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

        // If heavy selected, clear it
        if (HasHeavySelected())
        {
            ClearSelectionVisualsOnly();
            _selected.Clear();
        }

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

            SelectableBox aimedBox = null;

            if (requireAimAtSelectedToGrab)
            {
                // Forgiving aim so you can grab at the same distance you can select
                if (!TrySpherecastBox(interactRange, grabAimRadius, out aimedBox)) return;
                if (!_selected.Contains(aimedBox)) return;
            }
            else
            {
                aimedBox = _selected[0];
            }

            // Initialize grab distance from the grabbed object (so it doesn't feel "short")
            float distToBox = Vector3.Distance(cam.transform.position, aimedBox.transform.position);
            _grabDistance = Mathf.Clamp(distToBox, minGrabDistance, maxGrabDistance);

            // Update grabPoint immediately
            grabPoint.position = cam.transform.position + cam.transform.forward * _grabDistance;
            grabPoint.rotation = cam.transform.rotation;

            _isGrabbing = true;
            GrabStarted?.Invoke();
            GrabStartedThisFrame = true;

            _localOffsets.Clear();

            foreach (var b in _selected)
            {
                if (b.boxType == BoxType.Heavy)
                    b.heavyBaseY = b.transform.position.y;

                b.BeginHold();
                b.SetSelectedVisual();

                Vector3 offsetWorld = b.transform.position - grabPoint.position;
                Vector3 offsetLocal = Quaternion.Inverse(grabPoint.rotation) * offsetWorld;
                _localOffsets[b] = offsetLocal;
            }
        }

        if (!_isGrabbing) return;

        // Preview freeze while holding
        foreach (var b in _selected)
            if (_freezeMode) b.SetFrozenVisual();
            else b.SetSelectedVisual();

        // SCROLL push/pull (RAW + strong)
        Vector2 scrollVec = Mouse.current.scroll.ReadValue();
        float scrollRaw = scrollVec.y;

        if (Mathf.Abs(scrollRaw) > 0.01f)
        {
            ScrolledWhileHolding?.Invoke(scrollRaw);

            // scrollRaw is usually +/-120 per notch on many mice.
            // Use raw (strong), scaled by distance for that GMod feel.
            float dist01 = Mathf.InverseLerp(minGrabDistance, maxGrabDistance, _grabDistance);
            float distanceFactor = Mathf.Lerp(1f, 10f, dist01);

            float delta = scrollRaw * scrollSensitivity * scrollDistanceScale * distanceFactor;
            _grabDistance = Mathf.Clamp(_grabDistance + delta, minGrabDistance, maxGrabDistance);
        }

        // keep grabPoint anchored
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

            // freeze resets off after use
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

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, range, selectableMask, QueryTriggerInteraction.Ignore))
            return false;

        box = hit.collider.GetComponentInParent<SelectableBox>();
        return box != null;
    }

    bool TrySpherecastBox(float range, float radius, out SelectableBox box)
    {
        box = null;

        if (!Physics.SphereCast(cam.transform.position, radius, cam.transform.forward, out var hit, range, selectableMask, QueryTriggerInteraction.Ignore))
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
            if (b.IsFrozen) b.SetFrozenVisual();
            else b.SetNormalInstant();
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
