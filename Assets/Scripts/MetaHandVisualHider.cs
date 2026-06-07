using UnityEngine;

/// Disables every renderer under this object's hierarchy except those tagged as our tactical gloves.
/// Runs in LateUpdate for the first ~2s of play because the Meta Interaction SDK re-enables
/// hand visuals at runtime (OVRSkeletonRenderer + SkinnedMeshRenderer + ControllerHelper meshes).
public class MetaHandVisualHider : MonoBehaviour
{
    [SerializeField] private float keepHidingForSeconds = 5f;
    [SerializeField] private string keepNameContains = "TacticalGlove_";

    void Awake()  { HidePass(); }
    void Start()  { HidePass(); }
    void LateUpdate()
    {
        if (Time.timeSinceLevelLoad > keepHidingForSeconds) return;
        HidePass();
    }

    void HidePass()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (IsUnderKeep(r.transform)) continue;
            if (r.enabled) r.enabled = false;
        }
    }

    bool IsUnderKeep(Transform t)
    {
        while (t != null)
        {
            if (t.name.IndexOf(keepNameContains, System.StringComparison.Ordinal) >= 0) return true;
            t = t.parent;
        }
        return false;
    }
}
