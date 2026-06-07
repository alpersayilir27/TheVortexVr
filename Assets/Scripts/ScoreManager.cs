using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreBig;
    [SerializeField] private TMP_Text statsLine;
    [SerializeField] private WeaponShoot weapon;
    [SerializeField] private WaveManager waveManager;

    private int score;
    private float roundStart;

    void OnEnable()
    {
        Target.OnScored += AddScore;
        WaveManager.OnWaveStarted += HandleWaveStarted;
        WaveManager.OnWaveCleared += HandleWaveCleared;
    }

    void OnDisable()
    {
        Target.OnScored -= AddScore;
        WaveManager.OnWaveStarted -= HandleWaveStarted;
        WaveManager.OnWaveCleared -= HandleWaveCleared;
    }

    void Start() { roundStart = Time.time; RefreshAll(); }
    void Update() { RefreshAll(); }

    void AddScore(int v) { score += v; }
    void HandleWaveStarted(int w) { }
    void HandleWaveCleared(int w) { score += 100 * w; }

    void RefreshAll()
    {
        if (scoreBig != null) scoreBig.text = $"{score:D5}";

        if (statsLine == null) return;

        int wave = waveManager != null ? waveManager.CurrentWave : 1;
        int remaining = waveManager != null ? waveManager.TargetsRemaining : 0;

        string ammo;
        if (weapon == null) ammo = "--/--";
        else if (weapon.IsReloading) ammo = "RELOAD";
        else ammo = $"{weapon.CurrentAmmo:D2}/{weapon.MaxAmmo:D2}";

        int fired = weapon != null ? weapon.ShotsFired : 0;
        int hit = weapon != null ? weapon.ShotsHit : 0;
        float acc = fired > 0 ? (100f * hit / fired) : 0f;

        float t = Time.time - roundStart;
        int mm = (int)(t / 60f);
        int ss = (int)(t % 60f);

        // compact stadium-style: W3 · T02:15 · A24/30 · 72%
        string sep = "  <color=#666666>·</color>  ";
        statsLine.text =
            $"<color=#FFD75A>W</color>{wave}<size=60%> [{remaining}]</size>" + sep +
            $"<color=#FFD75A>T</color>{mm:D2}:{ss:D2}" + sep +
            $"<color=#FFD75A>A</color>{ammo}" + sep +
            $"<color=#FFD75A>{acc:F0}%</color>";
    }
}
