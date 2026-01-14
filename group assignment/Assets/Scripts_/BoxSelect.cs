using System.Collections;
using UnityEngine;

public enum BoxType { Small, Heavy }

[RequireComponent(typeof(Renderer))]
[RequireComponent(typeof(Rigidbody))]
public class SelectableBox : MonoBehaviour
{
    public BoxType boxType = BoxType.Small;

    [Header("Heavy box behavior")]
    public float heavyMaxLift = 0.25f;

    [Header("Materials (URP/Lit)")]
    public Material normalMaterial;
    public Material selectedMaterial;
    public Material frozenMaterial;

    [Header("Fade Back To Normal")]
    public float fadeBackSeconds = 0.25f;

    [Header("Physics Tuning (stops rubber / flying)")]
    [Tooltip("Caps how fast Unity can 'pop' objects apart when overlapping. Lower = less launching.")]
    public float maxDepenetrationVelocityDynamic = 2.0f;

    [Tooltip("Even lower while held to reduce pushing other boxes around.")]
    public float maxDepenetrationVelocityHeld = 0.75f;

    [Tooltip("Extra damping when released so stacks settle.")]
    public float dragDynamic = 0.2f;

    public float angularDragDynamic = 0.5f;

    [HideInInspector] public Rigidbody rb;
    [HideInInspector] public float heavyBaseY;

    public bool IsFrozen { get; private set; }

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private Coroutine _fadeRoutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // store original rb settings so we can restore cleanly
    private RigidbodyInterpolation _origInterpolation;
    private CollisionDetectionMode _origCollisionMode;
    private float _origDrag;
    private float _origAngularDrag;
    private float _origMaxDepenVel;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();

        heavyBaseY = transform.position.y;
        IsFrozen = false;

        // cache defaults
        _origInterpolation = rb.interpolation;
        _origCollisionMode = rb.collisionDetectionMode;
        _origDrag = rb.linearDamping;
        _origAngularDrag = rb.angularDamping;
        _origMaxDepenVel = rb.maxDepenetrationVelocity;

        // good defaults for boxes
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.maxDepenetrationVelocity = maxDepenetrationVelocityDynamic;
        rb.linearDamping = dragDynamic;
        rb.angularDamping = angularDragDynamic;

        SetNormalInstant();
    }

    // ---------------- Visuals ----------------

    public void SetNormalInstant()
    {
        StopFade();
        ClearPB();
        if (normalMaterial) _renderer.sharedMaterial = normalMaterial;
    }

    public void SetSelectedVisual()
    {
        StopFade();
        ClearPB();
        if (selectedMaterial) _renderer.sharedMaterial = selectedMaterial;
    }

    public void SetFrozenVisual()
    {
        StopFade();
        ClearPB();
        if (frozenMaterial) _renderer.sharedMaterial = frozenMaterial;
    }

    public void FadeToNormal()
    {
        StopFade();
        _fadeRoutine = StartCoroutine(FadeToNormalRoutine());
    }

    private IEnumerator FadeToNormalRoutine()
    {
        if (!normalMaterial) yield break;

        Color normalCol = normalMaterial.HasProperty(BaseColorId)
            ? normalMaterial.GetColor(BaseColorId)
            : Color.white;

        Material currentMat = _renderer.sharedMaterial;
        Color startCol = normalCol;

        if (currentMat != null && currentMat.HasProperty(BaseColorId))
            startCol = currentMat.GetColor(BaseColorId);

        _renderer.sharedMaterial = normalMaterial;

        float dur = Mathf.Max(0.0001f, fadeBackSeconds);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            Color c = Color.Lerp(startCol, normalCol, t);

            _mpb.SetColor(BaseColorId, c);
            _renderer.SetPropertyBlock(_mpb);
            yield return null;
        }

        ClearPB();
    }

    private void StopFade()
    {
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }

    private void ClearPB()
    {
        _mpb.Clear();
        _renderer.SetPropertyBlock(_mpb);
    }

    // ---------------- Physics States ----------------

    public void BeginHold()
    {
        if (IsFrozen) UnfreezeToDynamic();

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // holding: make it stable and reduce overlap 
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.None;

        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.maxDepenetrationVelocity = maxDepenetrationVelocityHeld;
    }

    public void DropDynamic()
    {
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rb.useGravity = true;

        // reduce “pop” on release
        rb.maxDepenetrationVelocity = maxDepenetrationVelocityDynamic;

        // add a touch of damping so stacks settle
        rb.linearDamping = dragDynamic;
        rb.angularDamping = angularDragDynamic;

        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public void FreezeHard()
    {
        IsFrozen = true;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.useGravity = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        // frozen should never “kick” things away
        rb.maxDepenetrationVelocity = maxDepenetrationVelocityHeld;

        SetFrozenVisual();
    }

    public void UnfreezeToDynamic()
    {
        IsFrozen = false;

        rb.constraints = RigidbodyConstraints.None;
        rb.isKinematic = false;
        rb.useGravity = true;

        rb.maxDepenetrationVelocity = maxDepenetrationVelocityDynamic;
        rb.linearDamping = dragDynamic;
        rb.angularDamping = angularDragDynamic;

        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }
}
