using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

// Player health for the zombie arena. Singleton so zombies can damage it from anywhere.
// Damage feedback: controller haptics + a red screen-edge vignette (a quad parented to
// the eye). On death: Game Over event + auto-restart (and manual restart with A/X).
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float regenDelay = 6f;     // seconds without damage before regen
    [SerializeField] private float regenRate = 6f;      // hp per second

    [Header("Feedback")]
    [SerializeField] private Renderer vignette;         // camera-parented red overlay
    [SerializeField] private float vignetteMaxAlpha = 0.65f;
    [SerializeField] private AudioClip hurtClip;
    [SerializeField] private AudioClip deathClip;

    [Header("Game Over")]
    [SerializeField] private float autoRestartDelay = 6f;

    private float health;
    private bool dead;
    private float lastDamageTime;
    private float flash;                                 // 0..1 transient damage flash
    private AudioSource audioSrc;
    private Material vignetteMat;

    public float Health01 => Mathf.Clamp01(health / maxHealth);
    public bool IsDead => dead;

    public static event System.Action<float> OnHealthChanged; // normalized 0..1
    public static event System.Action OnPlayerDied;
    public static event System.Action OnRestart;

    void Awake()
    {
        Instance = this;
        health = maxHealth;
        audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 0f; // 2D, it's the player
        if (vignette != null) vignetteMat = vignette.material; // instance
        SetVignette(0f);
    }

    void Start() => OnHealthChanged?.Invoke(Health01);

    public void TakeDamage(float dmg)
    {
        if (dead || dmg <= 0f) return;
        health -= dmg;
        lastDamageTime = Time.time;
        flash = 1f;

        if (hurtClip != null) audioSrc.PlayOneShot(hurtClip);
        StartCoroutine(Haptic(0.6f, 0.08f));
        OnHealthChanged?.Invoke(Health01);

        if (health <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (dead || amount <= 0f) return;
        health = Mathf.Min(maxHealth, health + amount);
        OnHealthChanged?.Invoke(Health01);
    }

    void Update()
    {
        if (!dead)
        {
            if (Time.time - lastDamageTime > regenDelay && health < maxHealth)
            {
                health = Mathf.Min(maxHealth, health + regenRate * Time.deltaTime);
                OnHealthChanged?.Invoke(Health01);
            }

            // vignette = transient damage flash, plus a steady pulse when health is low
            flash = Mathf.Max(0f, flash - Time.deltaTime * 2.2f);
            float low = 1f - Health01;
            float lowPulse = low > 0.6f ? (low - 0.6f) / 0.4f * (0.55f + 0.45f * Mathf.Sin(Time.time * 6f)) : 0f;
            SetVignette(Mathf.Clamp01(Mathf.Max(flash, lowPulse)) * vignetteMaxAlpha);
        }
        else
        {
            SetVignette(vignetteMaxAlpha);
            if (OVRInput.GetDown(OVRInput.Button.One) || OVRInput.GetDown(OVRInput.Button.Three))
                Restart();
        }
    }

    void Die()
    {
        dead = true;
        if (deathClip != null) audioSrc.PlayOneShot(deathClip);
        StartCoroutine(Haptic(1f, 0.4f));
        OnPlayerDied?.Invoke();
        StartCoroutine(AutoRestart());
    }

    IEnumerator AutoRestart()
    {
        yield return new WaitForSeconds(autoRestartDelay);
        if (dead) Restart();
    }

    void Restart()
    {
        // In-place reset (no scene-build-index dependency): reset health + tell listeners.
        StopAllCoroutines();
        dead = false;
        health = maxHealth;
        flash = 0f;
        SetVignette(0f);
        OnHealthChanged?.Invoke(Health01);
        OnRestart?.Invoke();
    }

    void SetVignette(float a)
    {
        if (vignetteMat == null) return;
        Color c = vignetteMat.HasProperty("_BaseColor") ? vignetteMat.GetColor("_BaseColor") : vignetteMat.color;
        c.a = a;
        if (vignetteMat.HasProperty("_BaseColor")) vignetteMat.SetColor("_BaseColor", c);
        vignetteMat.color = c;
    }

    IEnumerator Haptic(float amp, float dur)
    {
        OVRInput.SetControllerVibration(1f, amp, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(1f, amp, OVRInput.Controller.RTouch);
        yield return new WaitForSeconds(dur);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }
}
