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

    private static readonly Dictionary<long, List<PlayerSnapshot>> _buffer = new(1200);
    private static readonly List<long> _keysToRemove = new(1200);
    private static readonly List<long> _activeKeys = new(1200);
    private static readonly List<AntiCheatSnapshot> _batchSnapshots = new(1200);

    private static bool _isRunning;
    private static Coroutine? _workerCoroutine;
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

        if (!DataHandler.IsConfigured)
        {
            ReturnSnapshotToPool(snapshot);
            return;
        }

        if (!_buffer.TryGetValue(steamId, out var list))
        {
            list = new List<PlayerSnapshot>(64);
            _buffer[steamId] = list;
        }

        if (list.Count >= MAX_SNAPSHOTS_PER_PLAYER)
        {
            ReturnSnapshotToPool(snapshot);
            return;
        }

        list.Add(snapshot);
    }

    public static void StartWorker()
    {
        if (_isRunning) return;
        _isRunning = true;
        DataHandler.IsConfigured = ThoriumConfigService.HasValidToken;
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

        for (var i = 0; i < snapshots.Count; i++)
            ReturnSnapshotToPool(snapshots[i]);

        snapshots.Clear();
        _buffer.Remove(steamId);
    }

    public static void Reset()
    {
        StopWorker();

        foreach (var kvp in _buffer)
        {
            var snapshots = kvp.Value;
            for (var i = 0; i < snapshots.Count; i++)
                ReturnSnapshotToPool(snapshots[i]);
            snapshots.Clear();
        }

        _buffer.Clear();
    }

    private static IEnumerator WorkerRoutine()
    {
        while (_isRunning)
        {
            yield return _flushWait;
            DataHandler.IsConfigured = ThoriumConfigService.HasValidToken;
            if (_isRunning) FlushAll();
        }
    }

    private static void FlushAll()
    {
        _batchSnapshots.Clear();
        _keysToRemove.Clear();
        _activeKeys.Clear();

        // Phase 1: enumerate without modifying _buffer (assigning _buffer[key] increments version)
        foreach (var kvp in _buffer)
        {
            if (kvp.Value.Count == 0)
                _keysToRemove.Add(kvp.Key);
            else
                _activeKeys.Add(kvp.Key);
        }

        for (var i = 0; i < _keysToRemove.Count; i++)
            _buffer.Remove(_keysToRemove[i]);

        // Phase 2: swap outside enumeration
        for (var i = 0; i < _activeKeys.Count; i++)
        {
            var steamId = _activeKeys[i];
            var bufferList = _buffer[steamId];

            var acs = Pool.Get<AntiCheatSnapshot>();
            acs.SteamId = steamId;

            var emptyList = acs.Snapshots;
            acs.Snapshots = bufferList;
            _buffer[steamId] = emptyList;

            _batchSnapshots.Add(acs);
        }

        try
        {
            if (!DataHandler.IsConfigured)
                return;

            var caches = ThoriumEventPayload.TryDrainAndReset();
            var playerStats = PlayerServerStatsTracker.CollectDirtyStats(PlayerSnapshot.GetUnixTimestampMsCached());

            if (_batchSnapshots.Count == 0 && caches == null && playerStats.Count == 0)
                return;

            var batch = Pool.Get<ThoriumBatch>();
            batch.Snapshots.AddRange(_batchSnapshots);
            batch.PlayerServerStats.AddRange(playerStats);

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
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error flushing anti-cheat snapshots: {ex}");
        }
    }
}
