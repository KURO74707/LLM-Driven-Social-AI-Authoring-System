using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Builds a small random wall layout while leaving a clear box lane.</summary>
public class QH_RandomMapGenerator : MonoBehaviour
{
    [Header("Map size")]
    [SerializeField, Min(5)] private int width = 17;
    [SerializeField, Min(5)] private int depth = 17;
    [Tooltip("Used only when no scene layout groups are assigned.")]
    [SerializeField, Min(1)] private int wallCount = 26;
    [SerializeField, Min(0.5f)] private float cellSize = 1.5f;
    [SerializeField, Min(0.5f)] private float wallHeight = 1.5f;

    [Header("Random seed")]
    [SerializeField] private int seed = 583;
    [SerializeField] private bool chooseNewSeedOnPlay = true;

    [Header("Optional Inspector setup")]
    [SerializeField] private Transform wallsParent;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private NavMeshSurface navMeshSurface;
    [Tooltip("Agents in this scene that should be enabled only after its NavMesh is built. Leave empty for legacy scenes.")]
    [SerializeField] private NavMeshAgent[] agentsToBind;
    [SerializeField, Min(0.1f)] private float agentSnapDistance = 1.5f;
    [Tooltip("Optional scene-authored wall layouts. When assigned, no walls are instantiated.")]
    [SerializeField] private GameObject[] sceneLayouts;

    private readonly List<GameObject> spawnedWalls = new List<GameObject>();
    public bool IsMapGenerated { get; private set; }
    public int CurrentSeed { get { return seed; } }
    public string CurrentLayoutName { get; private set; }
    public float LastBuildMilliseconds { get; private set; }

    private void Start()
    {
        if (chooseNewSeedOnPlay)
            seed = UnityEngine.Random.Range(1, int.MaxValue);

        GenerateMap();
    }

    public void GenerateMap()
    {
        IsMapGenerated = false;
        if (agentsToBind != null)
            foreach (NavMeshAgent agent in agentsToBind)
                if (agent != null) agent.enabled = false;
        ClearOldWalls();

        if (sceneLayouts != null && sceneLayouts.Length > 0)
        {
            int selected = new System.Random(seed).Next(sceneLayouts.Length);
            for (int i = 0; i < sceneLayouts.Length; i++)
                if (sceneLayouts[i] != null) sceneLayouts[i].SetActive(i == selected);
            CurrentLayoutName = sceneLayouts[selected] == null ? "Missing layout" : sceneLayouts[selected].name;
            IsMapGenerated = BuildNavigation();
            return;
        }

        CurrentLayoutName = "Random walls";

        if (wallPrefab == null)
        {
            Debug.LogError("Assign a wall prefab in the Inspector.", this);
            return;
        }

        System.Random random = new System.Random(seed);
        int made = 0;
        int tries = 0;
        int maxTries = width * depth * 4;

        while (made < wallCount && tries < maxTries)
        {
            tries++;

            int x = random.Next(-width / 2, width / 2 + 1);
            int z = random.Next(-depth / 2, depth / 2 + 1);

            // Keep the center lane clear for the box and its target.
            if (Mathf.Abs(x) <= 1) continue;

            Vector3 position = new Vector3(x * cellSize, wallHeight * 0.5f, z * cellSize);
            GameObject wall = Instantiate(wallPrefab, position, Quaternion.identity);
            wall.name = "Random Wall";
            wall.transform.position = position;
            wall.transform.localScale = new Vector3(cellSize * 0.8f, wallHeight, cellSize * 0.8f);

            if (wallsParent != null)
                wall.transform.SetParent(wallsParent, true);

            spawnedWalls.Add(wall);
            made++;
        }

        IsMapGenerated = BuildNavigation();

        Debug.Log("[QH] Generated " + made + " walls with seed " + seed +
            ". NavMesh build: " + LastBuildMilliseconds.ToString("F1") + " ms.", this);
    }

    public void GenerateNewMap()
    {
        int oldLayout = sceneLayouts != null && sceneLayouts.Length > 1
            ? new System.Random(seed).Next(sceneLayouts.Length) : -1;
        seed = UnityEngine.Random.Range(1, int.MaxValue);
        if (oldLayout >= 0)
            for (int attempt = 0; attempt < 32 &&
                 new System.Random(seed).Next(sceneLayouts.Length) == oldLayout; attempt++)
                seed = UnityEngine.Random.Range(1, int.MaxValue);
        GenerateMap();
    }

    private bool BuildNavigation()
    {
        LastBuildMilliseconds = 0f;
        if (navMeshSurface == null)
        {
            if (agentsToBind == null || agentsToBind.Length == 0) return true;
            Debug.LogError("[QH] Assign a NavMeshSurface before binding scene agents.", this);
            return false;
        }
        float startTime = Time.realtimeSinceStartup;
        navMeshSurface.BuildNavMesh();
        LastBuildMilliseconds = (Time.realtimeSinceStartup - startTime) * 1000f;
        if (agentsToBind == null || agentsToBind.Length == 0) return true;
        if (navMeshSurface.navMeshData == null)
        {
            Debug.LogError("[QH] NavMesh build produced no data; scene agents remain disabled.", this);
            return false;
        }

        bool allBound = true;
        foreach (NavMeshAgent agent in agentsToBind)
        {
            if (agent == null)
            {
                Debug.LogError("[QH] A scene agent reference is missing.", this);
                allBound = false;
                continue;
            }
            Rigidbody body = agent.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) continue; // A shoved character belongs to physics until reset.
            if (!NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, agentSnapDistance, NavMesh.AllAreas))
            {
                Debug.LogError("[QH] No NavMesh near " + agent.name + " at " + agent.transform.position + ". Check its spawn and the surface's layer mask.", agent);
                allBound = false;
                continue;
            }
            agent.transform.position = hit.position;
            agent.enabled = true;
            if (!agent.isOnNavMesh || !agent.Warp(hit.position))
            {
                Debug.LogError("[QH] " + agent.name + " could not bind to the built NavMesh.", agent);
                agent.enabled = false;
                allBound = false;
            }
        }
        return allBound;
    }

    public void SetSeed(int newSeed)
    {
        seed = newSeed;
        chooseNewSeedOnPlay = false;
        GenerateMap();
    }

    private void ClearOldWalls()
    {
        foreach (GameObject wall in spawnedWalls)
        {
            if (wall == null) continue;
            wall.SetActive(false);
            if (Application.isPlaying) Destroy(wall);
            else DestroyImmediate(wall);
        }
        spawnedWalls.Clear();
    }
}
