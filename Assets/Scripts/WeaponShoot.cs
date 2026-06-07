using UnityEngine;
using System.Collections;
using Oculus.Interaction;

[RequireComponent(typeof(Grabbable))]
public class WeaponShoot : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private LineRenderer tracerTemplate;
    [SerializeField] private AudioSource shootSound;
    [SerializeField] private AudioSource dryFireSound;
    [SerializeField] private float shotSoundMaxDuration = 1.5f;

    [Header("Fire Tuning")]
    [SerializeField] private bool autoFire = false;
    [SerializeField] private float fireRate = 0.1f;
    [SerializeField] private float range = 100f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private float impactForce = 50f;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Ammo")]
    [SerializeField] private int maxAmmo = 30;
    [SerializeField] private int currentAmmo = 30;
    [SerializeField] private int reserveAmmo = -1; // spare rounds; -1 = infinite (default keeps MainScene unchanged)
    [SerializeField] private float reloadDuration = 1.5f;
    [SerializeField] private AudioSource reloadSound;
    [SerializeField] private float dryFireCooldown = 3f; // empty-click won't repeat faster than this

    [Header("Reload Animation")]
    [SerializeField] private Transform magazine;          // ejects/inserts during reload
    [SerializeField] private float reloadDipDepth = 0.04f; // how far the gun dips while reloading
    [SerializeField] private float magEjectDrop = 0.14f;   // how far the mag drops out

    [Header("Recoil")]
    [SerializeField] private float recoilKick = 0.04f;
    [SerializeField] private float recoilAngle = 3f;
    [SerializeField] private float recoilRecoverSpeed = 12f;
    [SerializeField] private float twoHandRecoilMultiplier = 0.4f;

    [Header("Tracer")]
    [SerializeField] private float tracerLifetime = 0.15f;

    private Grabbable grabbable;
    private float nextFireTime;
    private float nextDryFireTime;
    private bool isHeld;
    private bool prevTrigger;
    private Vector3 recoilOffset;
    private Vector3 recoilRot;
    private Vector3 reloadPos;
    private Vector3 reloadRot;
    private Vector3 magBaseLocalPos;
    private bool magCaptured;
    private Transform visualRoot;
    private Collider[] selfColliders;

    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => maxAmmo;
    public int ReserveAmmo => reserveAmmo;
    public bool IsHeld => isHeld;
    public bool IsReloading => isReloading;
    public void AddAmmo(int rounds) { if (reserveAmmo < 0 || rounds <= 0) return; reserveAmmo += rounds; }
    public int ShotsFired { get; private set; }
    public int ShotsHit { get; private set; }

    private bool isReloading;
    private float reloadEndTime;

    public static event System.Action<Vector3, Vector3, float, IDamageable> OnShotHit;

    public void TriggerReload() { if (!isReloading && currentAmmo < maxAmmo && reserveAmmo != 0) BeginReload(); }

    void Awake()
    {
        grabbable = GetComponent<Grabbable>();
        visualRoot = transform.childCount > 0 ? transform.GetChild(0) : transform;
        selfColliders = GetComponentsInChildren<Collider>(true);
    }

    void OnEnable()
    {
        grabbable.WhenPointerEventRaised += OnPointerEvent;
    }

    void OnDisable()
    {
        grabbable.WhenPointerEventRaised -= OnPointerEvent;
    }

    void OnPointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select) isHeld = grabbable.SelectingPointsCount > 0;
        else if (evt.Type == PointerEventType.Unselect) isHeld = grabbable.SelectingPointsCount > 0;
    }

    void Update()
    {
        ApplyRecoilDecay();

        if (isReloading)
        {
            if (Time.time >= reloadEndTime)
            {
                isReloading = false;
                int need = maxAmmo - currentAmmo;
                int take = reserveAmmo < 0 ? need : Mathf.Min(need, reserveAmmo);
                currentAmmo += take;
                if (reserveAmmo > 0) reserveAmmo -= take;
            }
            return;
        }

        if (!isHeld) { prevTrigger = false; return; }

        if (OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four))
        {
            TriggerReload();
            return;
        }

        if (currentAmmo <= 0 && OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.Active) > 0.5f)
        {
            // auto-reload if user keeps trying to fire while empty? Disabled by default.
        }

        float lt = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        float rt = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool trigger = lt > 0.7f || rt > 0.7f;

        bool shouldFire = autoFire ? trigger : (trigger && !prevTrigger);
        prevTrigger = trigger;

        if (!shouldFire) return;

        if (currentAmmo <= 0)
        {
            // empty: play the dry-fire click once, then not again for dryFireCooldown seconds
            if (Time.time >= nextDryFireTime)
            {
                if (dryFireSound != null && dryFireSound.clip != null) dryFireSound.PlayOneShot(dryFireSound.clip);
                nextDryFireTime = Time.time + dryFireCooldown;
            }
            return;
        }

        if (Time.time < nextFireTime) return;
        nextFireTime = Time.time + fireRate;
        Fire();
    }

    void BeginReload()
    {
        isReloading = true;
        reloadEndTime = Time.time + reloadDuration;
        if (reloadSound != null && reloadSound.clip != null) reloadSound.PlayOneShot(reloadSound.clip);
        StartCoroutine(ReloadRoutine());
    }

    // Procedural reload: the gun dips + tilts, the magazine drops out and a fresh one slides
    // back in over the reload time. Model-agnostic (no rigged reload needed).
    IEnumerator ReloadRoutine()
    {
        if (magazine != null && !magCaptured) { magBaseLocalPos = magazine.localPosition; magCaptured = true; }
        float dur = Mathf.Max(0.3f, reloadDuration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / dur);
            float pose = n < 0.3f ? n / 0.3f : (n > 0.7f ? 1f - (n - 0.7f) / 0.3f : 1f);
            reloadPos = new Vector3(0f, -reloadDipDepth, 0f) * pose;
            reloadRot = new Vector3(18f, 0f, -12f) * pose;

            if (magazine != null)
            {
                float magOut = n < 0.4f ? n / 0.4f : (n < 0.6f ? 1f : 1f - (n - 0.6f) / 0.4f);
                magazine.localPosition = magBaseLocalPos + Vector3.down * (magEjectDrop * magOut);
            }
            yield return null;
        }
        reloadPos = Vector3.zero;
        reloadRot = Vector3.zero;
        if (magazine != null) magazine.localPosition = magBaseLocalPos;
    }

    void Fire()
    {
        currentAmmo--;
        ShotsFired++;

        PlayShotSound();

        if (muzzleFlashPrefab != null && muzzle != null)
        {
            var flash = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzle);
            Destroy(flash, 0.08f);
        }

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 dir = muzzle != null ? muzzle.forward : transform.forward;

        Vector3 hitPoint = origin + dir * range;
        RaycastHit[] all = Physics.RaycastAll(origin, dir, range, hitMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in all)
        {
            if (IsSelfCollider(hit.collider)) continue;
            hitPoint = hit.point;

            if (hit.rigidbody != null)
            {
                hit.rigidbody.AddForceAtPosition(dir * impactForce, hit.point, ForceMode.Impulse);
            }

            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null)
            {
                dmg.TakeDamage(damage, hit.point, dir);
                ShotsHit++;
            }
            break;
        }

        DrawTracer(origin, hitPoint);
        ApplyRecoil();
        Haptics();
    }

    void DrawTracer(Vector3 from, Vector3 to)
    {
        // Cylinder mesh tracer — always renders, immune to URP LineRenderer quirks
        GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        // Disable rather than Destroy the collider — avoids Physics queries returning a dying-this-frame collider
        var tCol = t.GetComponent<Collider>();
        if (tCol != null) tCol.enabled = false;
        t.layer = 2; // IgnoreRaycast — extra safety
        Vector3 dir = to - from;
        float len = dir.magnitude;
        if (len < 0.01f) { Destroy(t); return; }
        t.transform.position = from + dir * 0.5f;
        t.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0);
        t.transform.localScale = new Vector3(0.025f, len * 0.5f, 0.025f);

        var mr = t.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        // Use any unlit shader and force bright color
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var mat = new Material(unlit);
        Color tracerColor = new Color(1f, 0.85f, 0.2f);
        mat.color = tracerColor;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tracerColor);
        mr.material = mat;

        Destroy(t, tracerLifetime);
    }

    void ApplyRecoil()
    {
        float mult = grabbable.SelectingPointsCount >= 2 ? twoHandRecoilMultiplier : 1f;
        recoilOffset += Vector3.back * recoilKick * mult;
        recoilRot += new Vector3(-recoilAngle * mult, Random.Range(-0.5f, 0.5f) * mult, 0);
    }

    void ApplyRecoilDecay()
    {
        recoilOffset = Vector3.Lerp(recoilOffset, Vector3.zero, Time.deltaTime * recoilRecoverSpeed);
        recoilRot = Vector3.Lerp(recoilRot, Vector3.zero, Time.deltaTime * recoilRecoverSpeed);
        if (visualRoot != null && visualRoot != transform)
        {
            visualRoot.localPosition = recoilOffset + reloadPos;
            visualRoot.localEulerAngles = recoilRot + reloadRot;
        }
    }

    void Haptics()
    {
        float mult = grabbable.SelectingPointsCount >= 2 ? 0.6f : 1f;
        OVRInput.SetControllerVibration(0.6f * mult, 0.8f * mult, OVRInput.Controller.RTouch);
        OVRInput.SetControllerVibration(0.6f * mult, 0.8f * mult, OVRInput.Controller.LTouch);
        Invoke(nameof(StopHaptics), 0.06f);
    }

    void StopHaptics()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
    }

    void PlayShotSound()
    {
        if (shootSound == null || shootSound.clip == null) return;
        // Spawn a disposable one-shot AudioSource so the clip is force-truncated to shotSoundMaxDuration.
        // PlayOneShot can't be stopped mid-clip; this approach guarantees no audio pile-up
        // even when the source recording is multi-second (e.g. the SKS clip contains a full burst).
        GameObject go = new GameObject("OneShot");
        Transform t = muzzle != null ? muzzle : transform;
        go.transform.position = t.position;
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = shootSound.clip;
        s.volume = shootSound.volume;
        s.pitch = Random.Range(0.95f, 1.05f); // slight pitch variation per shot
        s.spatialBlend = shootSound.spatialBlend;
        s.minDistance = shootSound.minDistance;
        s.maxDistance = shootSound.maxDistance;
        s.Play();
        Destroy(go, shotSoundMaxDuration);
    }

    bool IsSelfCollider(Collider c)
    {
        if (selfColliders == null) return false;
        for (int i = 0; i < selfColliders.Length; i++) if (selfColliders[i] == c) return true;
        return false;
    }

    public void Reload()
    {
        currentAmmo = maxAmmo;
    }
}

public interface IDamageable
{
    void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDir);
}
