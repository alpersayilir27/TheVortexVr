using UnityEngine;
using TMPro;

// Drives the hanging ScoreboardFace (health / wave / score / ammo), a camera-locked Game Over
// overlay, and a camera-locked "reload" warning. Listens to PlayerHealth / ZombieSpawner events.
public class ArenaHUD : MonoBehaviour
{
    [SerializeField] private GameObject gameOverOverlay;   // locked to the camera, always visible
    [SerializeField] private GameObject reloadWarning;     // camera-locked "change magazine" prompt
    [SerializeField] private TMP_Text reloadWarningText;

    private ScoreboardFace[] faces;
    private WeaponShoot[] weapons;

    void OnEnable()
    {
        PlayerHealth.OnHealthChanged += OnHealth;
        PlayerHealth.OnPlayerDied += OnDied;
        PlayerHealth.OnRestart += OnRestart;
        ZombieSpawner.OnWaveStarted += OnWave;
        ZombieSpawner.OnScoreChanged += OnScore;
    }

    void OnDisable()
    {
        PlayerHealth.OnHealthChanged -= OnHealth;
        PlayerHealth.OnPlayerDied -= OnDied;
        PlayerHealth.OnRestart -= OnRestart;
        ZombieSpawner.OnWaveStarted -= OnWave;
        ZombieSpawner.OnScoreChanged -= OnScore;
    }

    void Start()
    {
        faces = FindObjectsByType<ScoreboardFace>(FindObjectsSortMode.None);
        weapons = FindObjectsByType<WeaponShoot>(FindObjectsSortMode.None);
        if (gameOverOverlay != null) gameOverOverlay.SetActive(false);
        if (reloadWarning != null) reloadWarning.SetActive(false);
        OnScore(0);
        OnWave(1);
        OnHealth(1f);
    }

    void Update()
    {
        WeaponShoot held = null;
        if (weapons != null)
            foreach (var w in weapons) { if (w != null && w.IsHeld) { held = w; break; } }

        string ammo; bool warn = false; string warnText = "";
        if (held == null)
            ammo = "CEPHANE -";
        else if (held.IsReloading)
        {
            ammo = "<color=#FFCC33>ŞARJÖR DEĞİŞİYOR…</color>";
            warn = true; warnText = "ŞARJÖR DEĞİŞTİRİLİYOR…";
        }
        else if (held.CurrentAmmo <= 0)
        {
            ammo = "<color=#FF4040>ŞARJÖR BOŞ</color>";
            warn = true; warnText = "ŞARJÖR DEĞİŞTİR  (B / Y)";
        }
        else
        {
            string reserve = held.ReserveAmmo < 0 ? "∞" : held.ReserveAmmo.ToString();
            ammo = $"CEPHANE {held.CurrentAmmo} / {reserve}";
        }

        if (faces != null) foreach (var f in faces) if (f != null) f.SetAmmo(ammo);

        if (reloadWarning != null && reloadWarning.activeSelf != warn) reloadWarning.SetActive(warn);
        if (warn && reloadWarningText != null) reloadWarningText.text = warnText;
    }

    void OnHealth(float h) { if (faces != null) foreach (var f in faces) if (f != null) f.SetHealth(h); }
    void OnWave(int w) { if (faces != null) foreach (var f in faces) if (f != null) f.SetWave(w); }
    void OnScore(int s) { if (faces != null) foreach (var f in faces) if (f != null) f.SetScore(s); }

    void OnDied()
    {
        if (faces != null) foreach (var f in faces) if (f != null) f.SetGameOver(true);
        if (gameOverOverlay != null) gameOverOverlay.SetActive(true);
    }

    void OnRestart()
    {
        if (faces != null) foreach (var f in faces) if (f != null) f.SetGameOver(false);
        if (gameOverOverlay != null) gameOverOverlay.SetActive(false);
    }
}
