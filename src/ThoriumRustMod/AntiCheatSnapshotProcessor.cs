using System;
using System.Collections;
using System.Collections.Generic;
using Facepunch;
using ThoriumRustMod.Config;
using ThoriumRustMod.Core;
using ThoriumRustMod.Models;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod;

public static class AntiCheatSnapshotProcessor
{
    private const int FLUSH_INTERVAL_SECONDS = 1;
    private const int MAX_SNAPSHOTS_PER_PLAYER = 500;

    private static readonly Dictionary<long, Queue<PlayerSnapshot>> _buffer = new(1200);
    private static readonly List<long> _keysToRemove = new(1200);
    private static readonly List<AntiCheatSnapshot> _batchSnapshots = new(1200);

    private static bool _isRunning;
    private static Coroutine? _workerCoroutine;
    private static bool _isConfigured;
    private static float _lastConfigCheck;
    private static readonly WaitForSecondsRealtime _flushWait = new WaitForSecondsRealtime(FLUSH_INTERVAL_SECONDS);

    public static int BufferCount => _buffer.Count;
    public static bool IsWorkerRunning => _isRunning && _workerCoroutine != null;

    public static PlayerSnapshot GetPooledSnapshot()
    {
        return Pool.Get<PlayerSnapshot>();
    }

    private static void ReturnSnapshotToPool(PlayerSnapshot snapshot)
    {
        if (snapshot is null or EventSnapshot) return;
        Pool.Free(ref snapshot);
    }

    public static void Enqueue(long steamId, PlayerSnapshot snapshot)
    {
        if (steamId <= 0 || snapshot == null) return;

        var now = Time.realtimeSinceStartup;
        if (now - _lastConfigCheck > 5f)
        {
            _isConfigured = ThoriumConfigService.HasValidToken;
            _lastConfigCheck = now;
        }

        if (!_isConfigured)
        {
            ReturnSnapshotToPool(snapshot);
            return;
        }

        if (!_buffer.TryGetValue(steamId, out var queue))
        {
            queue = new Queue<PlayerSnapshot>(64);
            _buffer[steamId] = queue;
        }

        if (queue.Count >= MAX_SNAPSHOTS_PER_PLAYER)
        {
            var removed = queue.Dequeue();
            ReturnSnapshotToPool(removed);
        }

        queue.Enqueue(snapshot);
    }

    public static void StartWorker()
    {
        if (_isRunning) return;
        _isRunning = true;
        _isConfigured = ThoriumConfigService.HasValidToken;
        _lastConfigCheck = Time.realtimeSinceStartup;
        _workerCoroutine = ThoriumUnityScheduler.RunCoroutine(WorkerRoutine());
    }

    public static void StopWorker()
    {
        if (!_isRunning) return;
        _isRunning = false;

        ThoriumUnityScheduler.TryStopCoroutine(ref _workerCoroutine);

        FlushAll();
    }

    public static void CleanupPlayer(long steamId)
    {
        if (steamId <= 0)
            return;

        if (!_buffer.TryGetValue(steamId, out var snapshots))
            return;

        foreach (var snapshot in snapshots)
            ReturnSnapshotToPool(snapshot);

        snapshots.Clear();
        _buffer.Remove(steamId);
    }

    public static void Reset()
    {
        StopWorker();

        foreach (var kvp in _buffer)
        {
            var snapshots = kvp.Value;
            foreach (var snapshot in snapshots)
                ReturnSnapshotToPool(snapshot);
            snapshots.Clear();
        }

        _buffer.Clear();
    }

    private static IEnumerator WorkerRoutine()
    {
        while (_isRunning)
        {
            yield return _flushWait;
            if (_isRunning) FlushAll();
        }
    }

    private static void FlushAll()
    {
        _batchSnapshots.Clear();
        _keysToRemove.Clear();

        foreach (var kvp in _buffer)
        {
            var steamId = kvp.Key;
            var snapshots = kvp.Value;

            if (snapshots.Count == 0)
            {
                _keysToRemove.Add(steamId);
                continue;
            }

            AntiCheatSnapshot antiCheatSnapshot = Pool.Get<AntiCheatSnapshot>();

            antiCheatSnapshot.SteamId = steamId;
            antiCheatSnapshot.Snapshots.Clear();
            antiCheatSnapshot.Snapshots.AddRange(snapshots);

            _batchSnapshots.Add(antiCheatSnapshot);

            snapshots.Clear();
        }

        for (var i = 0; i < _keysToRemove.Count; i++)
            _buffer.Remove(_keysToRemove[i]);

        try
        {
            if (!ThoriumConfigService.HasValidToken)
                return;

            var caches = ThoriumEventPayload.TryDrainAndReset();

            if (_batchSnapshots.Count == 0 && caches == null)
                return;

            var batch = Pool.Get<ThoriumBatch>();
            batch.Snapshots.AddRange(_batchSnapshots);

            var payload = ThoriumBatchProtobufSerializer.Serialize(batch, caches);

            caches?.Return();

            ThoriumClientService.SendBinaryFireAndForget(payload);

            var batchRef = batch;
            Pool.Free(ref batchRef);

            foreach (var acs in _batchSnapshots)
            {
                for (var i = 0; i < acs.Snapshots.Count; i++)
                    ReturnSnapshotToPool(acs.Snapshots[i]);

                acs.Snapshots.Clear();
                var temp = acs;
                Pool.Free(ref temp);
            }
        } catch (Exception ex)
        {
            Debug.LogError($"Error flushing anti-cheat snapshots: {ex}");
        }
    }
}