using UnityEngine;

// A floating health/ammo pickup. Collected by walking near it (head) or reaching with a
// hand. Restores player health or tops up weapon reserve ammo, then respawns after a delay.
public class Pickup : MonoBehaviour
{
    public enum Kind { Health, Ammo }

    [SerializeField] private Kind kind = Kind.Health;
    [SerializeField] private float amount = 35f;          // hp, or rounds for ammo
    [SerializeField] private float pickupRadius = 0.55f;
    [SerializeField] private float respawnDelay = 20f;
    [SerializeField] private AudioClip pickupClip;

    [Header("Visual")]
    [SerializeField] private Transform visual;
    [SerializeField] private float bobHeight = 0.12f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float spinSpeed = 60f;

    private Transform head, leftHand, rightHand;
    private Vector3 visualBase;
    private bool collected;
    private float respawnAt;

    void Awake()
    {
        if (visual != null) visualBase = visual.localPosition;
    }

    void Start()
    {
        if (Camera.main != null) head = Camera.main.transform;
        leftHand = GameObject.Find("LeftHandAnchor")?.transform;
        rightHand = GameObject.Find("RightHandAnchor")?.transform;
    }

    void Update()
    {
        if (visual != null)
        {
            visual.localRotation = Quaternion.Euler(0f, Time.time * spinSpeed, 0f);
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
        }

        if (collected)
        {
            if (Time.time >= respawnAt) { collected = false; SetVisible(true); }
            return;
        }

        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (InReach()) Collect();
    }

    bool InReach()
    {
        Vector3 p = transform.position;
        // head: horizontal distance (walk near it)
        if (head != null)
        {
            Vector3 h = head.position; h.y = p.y;
            if (Vector3.Distance(p, h) <= pickupRadius) return true;
        }
        // hands: full 3D distance (reach for it)
        if (leftHand != null && Vector3.Distance(p, leftHand.position) <= pickupRadius) return true;
        if (rightHand != null && Vector3.Distance(p, rightHand.position) <= pickupRadius) return true;
        return false;
    }

    void Collect()
    {
        if (!Apply()) return; // don't consume if it wouldn't do anything (full health / infinite ammo)
        if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position, 1f);
        collected = true;
        respawnAt = Time.time + respawnDelay;
        SetVisible(false);
    }

    bool Apply()
    {
        if (kind == Kind.Health)
        {
            var ph = PlayerHealth.Instance;
            if (ph == null || ph.IsDead || ph.Health01 >= 1f) return false;
            ph.Heal(amount);
            return true;
        }
        else
        {
            bool any = false;
            foreach (var w in FindObjectsByType<WeaponShoot>(FindObjectsSortMode.None))
                if (w.ReserveAmmo >= 0) { w.AddAmmo((int)amount); any = true; }
            return any;
        }
    }

    void SetVisible(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
    }
}
