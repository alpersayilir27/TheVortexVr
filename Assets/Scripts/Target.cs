using UnityEngine;
using System.Collections;

public class Target : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 50f;

    [Header("Respawn")]
    [SerializeField] private bool respawn = true;
    [SerializeField] private float respawnDelay = 3f;

    [Header("Feedback")]
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip killClip;
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private Color hitFlashColor = Color.white;
    [SerializeField] private float hitFlashDuration = 0.08f;

    [Header("Scoring")]
    [SerializeField] private int scoreValue = 10;

    private float health;
    private Renderer rend;
    private Color baseColor;
    private AudioSource audioSrc;
    private Vector3 startPos;
    private Quaternion startRot;
    private Rigidbody rb;

    public static event System.Action<int> OnScored;
    public static event System.Action<Target> OnTargetDied;

    void Awake()
    {
        health = maxHealth;
        rend = GetComponentInChildren<Renderer>();
        if (rend != null) baseColor = rend.material.color;
        audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f;
        startPos = transform.position;
        startRot = transform.rotation;
        rb = GetComponent<Rigidbody>();
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDir)
    {
        health -= damage;

        if (hitVfxPrefab != null)
        {
            var vfx = Instantiate(hitVfxPrefab, hitPoint, Quaternion.LookRotation(-hitDir));
            Destroy(vfx, 1f);
        }

        if (hitClip != null) audioSrc.PlayOneShot(hitClip);
        StartCoroutine(FlashColor());

        if (health <= 0) Kill();
    }

    IEnumerator FlashColor()
    {
        if (rend == null) yield break;
        rend.material.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);
        rend.material.color = baseColor;
    }

    void Kill()
    {
        if (killClip != null) AudioSource.PlayClipAtPoint(killClip, transform.position);
        OnScored?.Invoke(scoreValue);
        OnTargetDied?.Invoke(this);

        if (respawn) StartCoroutine(RespawnRoutine());
        else gameObject.SetActive(false);
    }

    public void ResetTarget()
    {
        StopAllCoroutines();
        transform.SetPositionAndRotation(startPos, startRot);
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        health = maxHealth;
        if (rend != null) rend.material.color = baseColor;
        gameObject.SetActive(true);
    }

    public void SetSpawn(Vector3 pos)
    {
        startPos = pos;
        transform.position = pos;
    }

    IEnumerator RespawnRoutine()
    {
        gameObject.SetActive(false);
        yield return new WaitForSeconds(respawnDelay);
        transform.SetPositionAndRotation(startPos, startRot);
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        health = maxHealth;
        if (rend != null) rend.material.color = baseColor;
        gameObject.SetActive(true);
    }
}
