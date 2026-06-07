using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Spawns waves of zombies around the arena, away from the player. Scales difficulty
// per wave, pauses when the player dies, and resets on restart.
public class ZombieSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject zombiePrefab;

    [Header("Arena (XZ, world space)")]
    [SerializeField] private Vector2 arenaCenter = Vector2.zero;
    [SerializeField] private Vector2 arenaSize = new Vector2(8f, 14f);
    [SerializeField] private float floorY = 0f;
    [SerializeField] private float edgeInset = 1.2f;
    [SerializeField] private float minPlayerDistance = 4f;

    [Header("Waves")]
    [SerializeField] private int firstWaveCount = 4;
    [SerializeField] private int addedPerWave = 2;
    [SerializeField] private int maxAlive = 12;
    [SerializeField] private float spawnInterval = 1.2f;
    [SerializeField] private float interWaveDelay = 4f;
    [SerializeField] private float startDelay = 2f;

    [Header("Difficulty ramp")]
    [SerializeField] private float healthPerWave = 0.12f;  // +12% zombie HP each wave
    [SerializeField] private float speedPerWave = 0.03f;   // +3% speed each wave

    private int wave;
    private int aliveCount;
    private int totalScore;
    private bool paused;
    private Transform player;

    public int CurrentWave => wave;
    public int Score => totalScore;

    public static event System.Action<int> OnWaveStarted;
    public static event System.Action<int> OnScoreChanged;

    void OnEnable()
    {
        Zombie.OnZombieKilled += HandleKill;
        PlayerHealth.OnPlayerDied += HandlePlayerDied;
        PlayerHealth.OnRestart += HandleRestart;
    }

    void OnDisable()
    {
        Zombie.OnZombieKilled -= HandleKill;
        PlayerHealth.OnPlayerDied -= HandlePlayerDied;
        PlayerHealth.OnRestart -= HandleRestart;
    }

    void Start()
    {
        if (Camera.main != null) player = Camera.main.transform;
        if (zombiePrefab == null) { Debug.LogError("[ZombieSpawner] No zombiePrefab assigned."); return; }
        StartCoroutine(RunWave(firstWaveCount, startDelay));
    }

    void HandleKill(Zombie z, int score)
    {
        aliveCount = Mathf.Max(0, aliveCount - 1);
        totalScore += score;
        OnScoreChanged?.Invoke(totalScore);
        if (!paused && aliveCount == 0)
            StartCoroutine(RunWave(firstWaveCount + wave * addedPerWave, interWaveDelay));
    }

    void HandlePlayerDied() => paused = true;

    void HandleRestart()
    {
        StopAllCoroutines();
        foreach (var z in FindObjectsByType<Zombie>(FindObjectsSortMode.None))
            if (z != null) Destroy(z.gameObject);
        wave = 0;
        aliveCount = 0;
        totalScore = 0;
        paused = false;
        OnScoreChanged?.Invoke(0);
        StartCoroutine(RunWave(firstWaveCount, startDelay));
    }

    IEnumerator RunWave(int count, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (paused) yield break;
        wave++;
        OnWaveStarted?.Invoke(wave);

        for (int i = 0; i < count; i++)
        {
            while (aliveCount >= maxAlive) { if (paused) yield break; yield return new WaitForSeconds(0.5f); }
            if (paused) yield break;
            SpawnOne();
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnOne()
    {
        Vector3 pos = PickSpawn();
        var go = Instantiate(zombiePrefab, pos, Quaternion.identity, transform);
        go.name = $"Zombie_W{wave}_{aliveCount + 1}";
        var z = go.GetComponent<Zombie>();
        if (z != null) z.Init(1f + healthPerWave * (wave - 1), 1f + speedPerWave * (wave - 1));
        aliveCount++;
    }

    Vector3 PickSpawn()
    {
        float hx = arenaSize.x * 0.5f - edgeInset;
        float hz = arenaSize.y * 0.5f - edgeInset;
        Vector3 p = Vector3.zero;
        for (int tries = 0; tries < 12; tries++)
        {
            p = new Vector3(
                arenaCenter.x + Random.Range(-hx, hx),
                floorY,
                arenaCenter.y + Random.Range(-hz, hz));
            if (player == null) break;
            Vector3 flatPlayer = new Vector3(player.position.x, floorY, player.position.z);
            if (Vector3.Distance(p, flatPlayer) >= minPlayerDistance) break;
        }
        return p;
    }
}
