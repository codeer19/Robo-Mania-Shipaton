using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prefab-keyed reuse for the objects combat creates most often: projectiles,
/// impact effects, muzzle effects and damage numbers. These were previously
/// Instantiated and Destroyed per shot and per hit, which is the main source of
/// managed allocation and GC spikes during a fight on mobile.
///
/// Instances are parented to a persistent root while idle so they do not appear
/// in the active hierarchy, and the pool is cleared automatically when play mode
/// ends so nothing leaks between sessions.
/// </summary>
public static class CombatPool
{
    private static readonly Dictionary<GameObject, Stack<GameObject>> Available =
        new Dictionary<GameObject, Stack<GameObject>>();

    private static readonly Dictionary<GameObject, GameObject> SourcePrefab =
        new Dictionary<GameObject, GameObject>();

    // GetComponentsInChildren allocated a fresh array on every Get and Release,
    // i.e. per shot and per impact. The component set of a pooled instance never
    // changes, so it is looked up once.
    private static readonly Dictionary<GameObject, IPooledObject[]> PooledParts =
        new Dictionary<GameObject, IPooledObject[]>();

    /// <summary>Call after adding an IPooledObject component to a pooled instance at runtime.</summary>
    public static void RefreshParts(GameObject instance)
    {
        if (instance != null) PooledParts[instance] = instance.GetComponentsInChildren<IPooledObject>(true);
    }

    private static IPooledObject[] Parts(GameObject instance)
    {
        if (!PooledParts.TryGetValue(instance, out IPooledObject[] parts))
        {
            parts = instance.GetComponentsInChildren<IPooledObject>(true);
            PooledParts[instance] = parts;
        }
        return parts;
    }

    private static Transform poolRoot;

    private static Transform Root
    {
        get
        {
            if (poolRoot == null)
            {
                GameObject rootObject = new GameObject("~CombatPool");
                rootObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(rootObject);
                rootObject.SetActive(false);
                poolRoot = rootObject.transform;
            }

            return poolRoot;
        }
    }

    public static GameObject Get(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null)
    {
        if (prefab == null)
            return null;

        if (PrivateRoomAIAudit.ContainsAI(prefab) && !BotMatchPolicy.RequireBotMatch("CombatPool.Get", prefab))
            return null;

        GameObject instance = null;

        if (Available.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            while (stack.Count > 0 && instance == null)
                instance = stack.Pop();
        }

        if (instance == null)
        {
            instance = Object.Instantiate(prefab, position, rotation, parent);
            SourcePrefab[instance] = prefab;
        }
        else
        {
            instance.transform.SetParent(parent, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
        }

        foreach (IPooledObject pooled in Parts(instance))
        {
            pooled.OnRetrievedFromPool();
        }

        return instance;
    }

    public static T Get<T>(
        T prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null) where T : Component
    {
        if (prefab == null)
            return null;

        GameObject instance = Get(prefab.gameObject, position, rotation, parent);
        return instance != null ? instance.GetComponent<T>() : null;
    }

    public static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        if (!SourcePrefab.TryGetValue(instance, out GameObject prefab))
        {
            // Created outside the pool. Destroying keeps behaviour identical to
            // the old code path rather than silently leaking the object.
            Object.Destroy(instance);
            return;
        }

        foreach (IPooledObject pooled in Parts(instance))
        {
            pooled.OnReturnedToPool();
        }

        instance.SetActive(false);
        instance.transform.SetParent(Root, false);

        if (!Available.TryGetValue(prefab, out Stack<GameObject> stack))
        {
            stack = new Stack<GameObject>();
            Available[prefab] = stack;
        }

        stack.Push(instance);
    }

    /// <summary>
    /// Pre-creates instances so the first shots of a match do not pay for
    /// instantiation. Safe to call more than once.
    /// </summary>
    public static void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
            return;

        for (int index = 0; index < count; index++)
        {
            GameObject instance = Get(prefab, Vector3.zero, Quaternion.identity);
            Release(instance);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        Available.Clear();
        SourcePrefab.Clear();
        PooledParts.Clear();
        poolRoot = null;
    }
}

/// <summary>
/// Implemented by pooled components that need to reset per-use state.
/// </summary>
public interface IPooledObject
{
    void OnRetrievedFromPool();
    void OnReturnedToPool();
}
