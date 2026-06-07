using UnityEngine;
using Oculus.Interaction;

[RequireComponent(typeof(Grabbable))]
public class WeaponSocket : MonoBehaviour
{
    [SerializeField] private Transform socketHome;
    [SerializeField] private float snapRange = 0.35f;       // socket pulls weapon when this close
    [SerializeField] private float snapLerpSpeed = 14f;
    [SerializeField] private float snapDistance = 0.04f;    // counts as docked
    [SerializeField] private Renderer socketGlowRenderer;    // optional disc/ring renderer at the socket
    [SerializeField] private Color glowColor = new Color(1f, 0.85f, 0.2f);

    private Grabbable grabbable;
    private Rigidbody rb;
    private bool held;
    private bool docking;
    private bool docked;

    public bool IsDocked => docked;

    void Awake()
    {
        grabbable = GetComponent<Grabbable>();
        rb = GetComponent<Rigidbody>();
        SnapHome();
    }

    void OnEnable() { grabbable.WhenPointerEventRaised += OnPointer; }
    void OnDisable() { grabbable.WhenPointerEventRaised -= OnPointer; }

    void OnPointer(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select)
        {
            held = grabbable.SelectingPointsCount > 0;
            if (held) UndockAndEnablePhysics();
        }
        else if (evt.Type == PointerEventType.Unselect)
        {
            held = grabbable.SelectingPointsCount > 0;
            // released completely — gravity drops it; socket only takes it back when brought close
        }
    }

    void UndockAndEnablePhysics()
    {
        docked = false;
        docking = false;
        if (rb != null) { rb.isKinematic = false; rb.useGravity = true; }
    }

    void Update()
    {
        if (socketHome == null) return;

        float distToSocket = Vector3.Distance(transform.position, socketHome.position);

        // --- Glow brightness while approaching ---
        UpdateGlow(distToSocket);

        if (held) { docking = false; return; }
        if (docked) return;

        // Within snap range → lerp into the socket
        if (distToSocket < snapRange)
        {
            docking = true;
            if (rb != null) { rb.useGravity = false; rb.linearVelocity *= 0.3f; }
            transform.position = Vector3.Lerp(transform.position, socketHome.position, Time.deltaTime * snapLerpSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, socketHome.rotation, Time.deltaTime * snapLerpSpeed);

            if (distToSocket < snapDistance) SnapHome();
        }
        else
        {
            // outside snap range — let gravity drop it naturally
            docking = false;
            if (rb != null && rb.isKinematic == false) rb.useGravity = true;
        }
    }

    void UpdateGlow(float dist)
    {
        if (socketGlowRenderer == null) return;
        float t = held ? 1f : (docked ? 0f : Mathf.Clamp01(1f - dist / (snapRange * 1.6f)));
        Color c = glowColor * (held ? 0.6f : Mathf.Lerp(0.0f, 1.4f, t));
        c.a = Mathf.Lerp(0.05f, 0.85f, t);
        var mat = socketGlowRenderer.material;
        mat.color = c;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 3f);
    }

    void SnapHome()
    {
        transform.position = socketHome.position;
        transform.rotation = socketHome.rotation;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        docking = false;
        docked = true;
    }
}
