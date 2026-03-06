using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ThoriumRustMod.Core;

public sealed class ThoriumUnityScheduler : MonoBehaviour
{
    private static ThoriumUnityScheduler? _instance;
    private static int _mainThreadId;
    private static readonly object _queueLock = new();
    private static readonly Queue<IEnumerator> _pendingCoroutines = new();

    public static bool IsInitialized => _instance != null;

    public static ThoriumUnityScheduler Instance
    {
        get
        {
            if (_instance == null)
                EnsureInitialized();

            return _instance!;
        }
    }

    public static void EnsureInitialized()
    {
        if (_instance != null)
            return;

        try
        {
            var go = new GameObject("Thorium.UnityScheduler");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ThoriumUnityScheduler>();
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Thorium] Failed to initialize Unity scheduler: {ex.Message}");
            throw;
        }
    }

    public static Coroutine? RunCoroutine(IEnumerator routine)
    {
        if (routine == null)
            return null;

        if (_instance != null && Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            return _instance.StartCoroutine(routine);

        lock (_queueLock)
        {
            _pendingCoroutines.Enqueue(routine);
        }

        return null;
    }

    public static void TryStopCoroutine(ref Coroutine? coroutine)
    {
        if (coroutine == null) return;
        try
        {
            if (IsInitialized)
                Instance.StopCoroutine(coroutine);
        }
        catch
        {
        }

        coroutine = null;
    }

    public static void DestroyInstance()
    {
        if (_instance == null)
            return;

        try
        {
            var go = _instance.gameObject;
            _instance = null;
            if (go != null)
                Destroy(go);
        }
        catch
        {
            _instance = null;
        }
    }

    private void Update()
    {
        while (true)
        {
            IEnumerator? routine = null;
            lock (_queueLock)
            {
                if (_pendingCoroutines.Count > 0)
                    routine = _pendingCoroutines.Dequeue();
            }

            if (routine == null)
                break;

            StartCoroutine(routine);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}