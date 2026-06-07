using UnityEngine;
using System.Collections;

// A shootable, walking zombie. Implements IDamageable so the existing WeaponShoot
// raycast hits it unchanged. Walks toward the player, and once in range stops and
// bites for periodic damage. Headshots do bonus damage. Difficulty scales per wave.
[RequireComponent(typeof(Collider))]
public class Zombie : MonoBehaviour, IDamageable
{
    [Header("Health / Scoring")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private int scoreValue = 10;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 0.5f;       // slow shamble
    [SerializeField] private float speedVariation = 0.2f;  // +/- fraction, per zombie
    [SerializeField] private float turnSpeed = 4f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.4f;     // starts biting within this
    [SerializeField] private float attackInterval = 1.3f;  // seconds between bites
    [SerializeField] private float attackDamage = 9f;
    [SerializeField] private float firstBiteDelay = 0.5f;  // wind-up before first bite lands

    [Header("Headshot")]
    [SerializeField] private float headHeight = 1.45f;     // local Y above which counts as head
    [SerializeField] private float headshotMultiplier = 3f;

    [Header("Feedback")]
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.3f, 0.3f);
    [SerializeField] private float hitFlashDuration = 0.07f;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip deathClip;
    [SerializeField] private AudioClip biteClip;
    [SerializeField] private AudioClip[] groanClips;
    [SerializeField] private Vector2 groanInterval = new Vector2(3f, 8f); // random seconds between groans

    private static readonly int AttackingHash = Animator.StringToHash("Attacking");

    private float health;
    private bool dead;
    private bool gotHeadshotKill;
    private Transform player;
    private Animator animator;
    private Renderer[] renderers;
    private MaterialPropertyBlock mpb;
    private AudioSource audioSrc;
    private float nextAttackTime;
    private float nextGroanTime;

    // (zombie, score) — spawner tracks wave progress and scoring from this.
    public static event System.Action<Zombie, int> OnZombieKilled;

    void Awake()
    {
        health = maxHealth;
        moveSpeed *= 1f + Random.Range(-speedVariation, speedVariation);
        animator = GetComponentInChildren<Animator>();
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 1.5f;
        audioSrc.maxDistance = 40f;
    }

    // Called by the spawner right after Instantiate to scale difficulty by wave.
    public void Init(float healthMultiplier, float speedMultiplier)
    {
        maxHealth *= healthMultiplier;
        health = maxHealth;
        moveSpeed *= speedMultiplier;
    }

    void Start() => AcquirePlayer();

    void AcquirePlayer()
    {
        if (Camera.main != null) { player = Camera.main.transform; return; }
        var rig = GameObject.Find("CenterEyeAnchor");
        if (rig != null) player = rig.transform;
    }

    void Update()
    {
        if (dead) return;
        if (player == null) { AcquirePlayer(); return; }
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead)
        {
            if (animator != null) animator.SetBool(AttackingHash, false);
            return; // stand down when the player is dead
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        if (dist > 0.05f)
        {
            Quaternion want = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, Time.deltaTime * turnSpeed);
        }

        bool inRange = dist <= attackRange;
        if (animator != null) animator.SetBool(AttackingHash, inRange);

        if (inRange)
        {
            if (nextAttackTime == 0f) nextAttackTime = Time.time + firstBiteDelay;
            if (Time.time >= nextAttackTime)
            {
                PlayerHealth.Instance?.TakeDamage(attackDamage);
                if (biteClip != null) audioSrc.PlayOneShot(biteClip);
                nextAttackTime = Time.time + attackInterval;
            }
        }
        else
        {
            nextAttackTime = 0f;
            transform.position += transform.forward * moveSpeed * Time.deltaTime;
        }

        MaybeGroan();
    }

    void MaybeGroan()
    {
        if (groanClips == null || groanClips.Length == 0) return;
        if (nextGroanTime == 0f) { nextGroanTime = Time.time + Random.Range(0f, groanInterval.y); return; }
        if (Time.time < nextGroanTime) return;
        audioSrc.PlayOneShot(groanClips[Random.Range(0, groanClips.Length)], 0.7f);
        nextGroanTime = Time.time + Random.Range(groanInterval.x, groanInterval.y);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDir)
    {
        if (dead) return;

        bool headshot = (hitPoint.y - transform.position.y) >= headHeight;
        if (headshot) damage *= headshotMultiplier;
        health -= damage;

        if (hitVfxPrefab != null)
        {
            var vfx = Instantiate(hitVfxPrefab, hitPoint, Quaternion.LookRotation(-hitDir));
            Destroy(vfx, 1.5f);
        }
        if (hitClip != null) audioSrc.PlayOneShot(hitClip);
        StartCoroutine(Flash(headshot ? Color.red : hitFlashColor));

        if (health <= 0f) { gotHeadshotKill = headshot; Die(); }
    }

    IEnumerator Flash(Color c)
    {
        SetColor(c);
        yield return new WaitForSeconds(hitFlashDuration);
        SetColor(Color.white);
    }

    void SetColor(Color c)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor"))
                mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
        }
    }

    void Die()
    {
        dead = true;
        int score = gotHeadshotKill ? scoreValue * 2 : scoreValue;
        OnZombieKilled?.Invoke(this, score);

        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
        if (animator != null) animator.enabled = false;

        if (deathVfxPrefab != null)
        {
            var vfx = Instantiate(deathVfxPrefab, transform.position + Vector3.up, Quaternion.identity);
            Destroy(vfx, 2f);
        }
        if (deathClip != null) AudioSource.PlayClipAtPoint(deathClip, transform.position);

        StartCoroutine(SinkAndDestroy());
    }

    IEnumerator SinkAndDestroy()
    {
        float t = 0f;
        Vector3 start = transform.position;
        Quaternion startRot = transform.rotation;
        Quaternion fallRot = startRot * Quaternion.Euler(85f, 0f, 0f);
        while (t < 1.4f)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, fallRot, Mathf.Clamp01(t * 1.5f));
            if (t > 0.8f) transform.position = start + Vector3.down * ((t - 0.8f) * 0.8f);
            yield return null;
        }
        Destroy(gameObject);
    }
}
