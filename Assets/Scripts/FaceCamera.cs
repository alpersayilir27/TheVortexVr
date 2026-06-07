using UnityEngine;

// Keeps a world-space UI canvas turned so its text always reads correctly toward the
// player, from any position — fixes mirrored/!backwards HUD text in VR. Yaw-only by
// default so a wall panel stays upright.
[DefaultExecutionOrder(1000)]
public class FaceCamera : MonoBehaviour
{
    [SerializeField] private bool yawOnly = true;
    private Transform cam;

    void LateUpdate()
    {
        if (cam == null)
        {
            if (Camera.main != null) cam = Camera.main.transform;
            else return;
        }

        // A UI canvas reads correctly when its forward points AWAY from the viewer.
        Vector3 dir = transform.position - cam.position;
        if (yawOnly) dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}
