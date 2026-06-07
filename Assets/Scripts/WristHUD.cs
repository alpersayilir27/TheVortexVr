using UnityEngine;
using TMPro;

public class WristHUD : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private float showAngle = 50f;       // angle below which HUD is visible
    [SerializeField] private float fadeSpeed = 8f;
    [SerializeField] private bool alwaysVisible = false;
    [SerializeField] private Transform cameraOverride;    // optional: assign CenterEyeAnchor

    private Transform cam;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
    }

    void Start()
    {
        cam = cameraOverride != null ? cameraOverride : (Camera.main != null ? Camera.main.transform : null);
    }

    void LateUpdate()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        float target = 1f;
        if (!alwaysVisible && cam != null)
        {
            // HUD is on the back of the wrist; reveals when the user rotates the wrist so the HUD faces the eyes.
            Vector3 hudNormal = transform.forward;
            Vector3 toEye = (cam.position - transform.position).normalized;
            float dot = Vector3.Dot(hudNormal, toEye);
            float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
            target = angle < showAngle ? 1f : 0f;
        }
        group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.deltaTime);
    }
}
