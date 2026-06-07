using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    [SerializeField] private Transform targetsRoot;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private Vector2 arenaSize = new Vector2(7f, 5f);
    [SerializeField] private float arenaForwardOffset = 3.5f;
    [SerializeField] private float minHeight = 1f;
    [SerializeField] private float maxHeight = 2.2f;
    [SerializeField] private float interWaveDelay = 2.5f;
    [SerializeField] private int targetsInFirstWave = 5;
    [SerializeField] private int targetsAddedPerWave = 2;

    private List<Target> active = new List<Target>();
    private int wave;

    public int CurrentWave => wave;
    public int TargetsRemaining => active.Count;

    public static event System.Action<int> OnWaveStarted;
    public static event System.Action<int> OnWaveCleared;

    void OnEnable() { Target.OnTargetDied += HandleDied; }
    void OnDisable() { Target.OnTargetDied -= HandleDied; }

    void Start() { StartCoroutine(StartFirstWave()); }

    IEnumerator StartFirstWave()
    {
        yield return new WaitForSeconds(1f);
        SpawnWave(targetsInFirstWave);
    }

    void HandleDied(Target t)
    {
        active.Remove(t);
        if (active.Count == 0)
        {
            OnWaveCleared?.Invoke(wave);
            StartCoroutine(NextWaveAfterDelay());
        }
    }

    IEnumerator NextWaveAfterDelay()
    {
        yield return new WaitForSeconds(interWaveDelay);
        SpawnWave(targetsInFirstWave + wave * targetsAddedPerWave);
    }

    void SpawnWave(int count)
    {
        wave++;
        ClearStragglers();

        if (targetPrefab != null)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = RandomArenaPos();
                var go = Instantiate(targetPrefab, pos, Quaternion.identity, targetsRoot != null ? targetsRoot : transform);
                go.name = $"Wave{wave}_Target_{i + 1}";
                var t = go.GetComponent<Target>();
                if (t != null) active.Add(t);
            }
        }
        else if (targetsRoot != null)
        {
            foreach (Transform child in targetsRoot)
            {
                var t = child.GetComponent<Target>();
                if (t == null) continue;
                t.ResetTarget();
                t.SetSpawn(RandomArenaPos());
                active.Add(t);
                if (active.Count >= count) break;
            }
        }

        OnWaveStarted?.Invoke(wave);
    }

    Vector3 RandomArenaPos()
    {
        return new Vector3(
            Random.Range(-arenaSize.x * 0.5f, arenaSize.x * 0.5f),
            Random.Range(minHeight, maxHeight),
            arenaForwardOffset + Random.Range(0f, arenaSize.y)
        );
    }

    void ClearStragglers()
    {
        foreach (var t in active) if (t != null) Destroy(t.gameObject);
        active.Clear();
    }
}
