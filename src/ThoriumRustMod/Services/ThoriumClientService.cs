using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConVar;
using Newtonsoft.Json;
using ThoriumRustMod.Config;
using ThoriumRustMod.Core;
using ThoriumRustMod.Models;
using UnityEngine;

namespace ThoriumRustMod.Services;

public static class ThoriumClientService
{

    private const int BUFFER_SIZE = 4096;
    private const int RECONNECT_INTERVAL_SECONDS = 10;
    private const int MAX_RECONNECT_INTERVAL_SECONDS = 120;
    private const string SERVER_TOKEN_HEADER = "X-SERVER-TOKEN";
    private const string SESSION_TOKEN_HEADER = "X-SESSION-TOKEN";
    private const string AUTH_ENDPOINT = "/api/session/auth";
    private const string WS_ENDPOINT = "/api/anticheat/ws";

    public static string token => ThoriumConfigService.ServerToken;

    private static ClientWebSocket? _webSocket;
    private static bool _isConnected;
    private static bool _isConnecting;
    private static int _reconnectAttempts;
    private static string? _currentUri;
    private static string? _sessionToken;
    private static Coroutine? _receiveCoroutine;
    private static Coroutine? _reconnectCoroutine;
    private static readonly Queue<SendItem> _sendQueue = new(128);
    private static Coroutine? _sendLoopCoroutine;
    private const int MAX_SEND_QUEUE = 10;
    private static Models.ServerInfo? _serverInfo;
    private static string _mapHash = string.Empty;
    private static volatile bool _isResyncing;

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly struct SendItem
    {
        public readonly byte[] Data;
        public readonly WebSocketMessageType Type;
        public readonly TaskCompletionSource<bool>? Tcs;

        public SendItem(byte[] data, WebSocketMessageType type, TaskCompletionSource<bool>? tcs)
        {
            Data = data;
            Type = type;
            Tcs = tcs;
        }
    }

    public static event Action<string>? OnMessageReceived;

    public static event Action<byte[]>? OnBinaryMessageReceived;

    public static event Action? OnConnected;

    public static event Action? OnDisconnected;

    public static void SubscribeResync() => OnMessageReceived += HandleResyncMessage;
    public static void UnsubscribeResync() => OnMessageReceived -= HandleResyncMessage;

    private static void HandleResyncMessage(string json)
    {
        ResyncMessage? msg;
        try { msg = JsonConvert.DeserializeObject<ResyncMessage>(json); }
        catch { return; }

        if (msg?.Type != "resync") return;

        if (!IsConnected)
        {
            Log.Debug(() => "Received resync request but not connected, ignoring");
            return;
        }

        if (_isResyncing)
        {
            Log.Debug(() => "Resync already in progress, ignoring duplicate request");
            return;
        }

        Log.Info("Received resync request from backend, resending initial data");
        _isResyncing = true;
        ThoriumUnityScheduler.RunCoroutine(ResyncRoutine());
    }

    private static IEnumerator ResyncRoutine()
    {
        // Resend server info
        if (_serverInfo != null)
        {
            TaskCompletionSource<bool>? tcs = null;
            try
            {
                var json = JsonConvert.SerializeObject(_serverInfo);
                Log.Debug(() => $"Resync: sending server info");
                tcs = new TaskCompletionSource<bool>();
                EnqueueSend(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, tcs);
            }
            catch (Exception ex)
            {
                Log.Error($"Resync: error serializing server info: {ex.Message}");
            }

            if (tcs != null)
            {
                while (!tcs.Task.IsCompleted)
                    yield return null;
            }
        }

        // Resend entities and player baselines
        ThoriumUnityScheduler.RunCoroutine(SendInitialEntitiesRoutine());
        ThoriumUnityScheduler.RunCoroutine(SendInitialPlayerBaselineRoutine());

        _isResyncing = false;
        Log.Info("Resync: initial data resend triggered");
    }

    private class ResyncMessage
    {
        [JsonProperty("type")] public string? Type { get; set; }
    }

    public static void SetServerInfo(Models.ServerInfo serverInfo)
    {
        _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
    }

    public static async Task ConnectAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("URI cannot be null or empty", nameof(uri));

        if (_isConnected || _isConnecting)
        {
            return;
        }

        _isConnecting = true;
        _currentUri = uri;

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(ConnectRoutine(uri, tcs));

        await tcs.Task;
    }

    public static void EnsureReconnectLoopRunning()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentUri))
                return;

            if (_reconnectCoroutine != null)
                return;

            _reconnectCoroutine = ThoriumUnityScheduler.RunCoroutine(ReconnectLoopRoutine());
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"EnsureReconnectLoopRunning error: {ex.Message}");
        }
    }

    public static async Task SendMessageAsync(string message)
    {
        if (string.IsNullOrEmpty(message))
            throw new ArgumentException("Message cannot be null or empty", nameof(message));

        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var tcs = new TaskCompletionSource<bool>();
        EnqueueSend(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, tcs);
        await tcs.Task;
    }

    public static void SendBinaryFireAndForget(byte[] data)
    {
        if (data is not { Length: > 0 }) return;
        EnqueueSend(data, WebSocketMessageType.Binary, null);
    }

    public static async Task SendBinaryAsync(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var tcs = new TaskCompletionSource<bool>();
        EnqueueSend(data, WebSocketMessageType.Binary, tcs);
        await tcs.Task;
    }

    public static async Task SendBinaryOrQueueAsync(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (!IsConnected)
        {
            EnqueueSend(data, WebSocketMessageType.Binary, null);
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        EnqueueSend(data, WebSocketMessageType.Binary, tcs);

        try
        {
            await tcs.Task;
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"Binary send failed: {ex.Message}");
        }
    }

    public static async Task SendJsonAsync<T>(T message) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var json = JsonConvert.SerializeObject(message);

        if (!IsConnected)
        {
            EnqueueSend(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, null);
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        EnqueueSend(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, tcs);

        try
        {
            await tcs.Task;
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"Send failed: {ex.Message}");
        }
    }

    public static int PendingQueueCount => _sendQueue.Count;

    public static T? DeserializeJson<T>(string json) where T : class
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to deserialize JSON: {ex.Message}");
            return null;
        }
    }

    private static void StartSendLoop()
    {
        if (_sendLoopCoroutine != null) return;
        _sendLoopCoroutine = ThoriumUnityScheduler.RunCoroutine(SendLoopRoutine());
    }

    private static void StopSendLoop()
    {
        ThoriumUnityScheduler.TryStopCoroutine(ref _sendLoopCoroutine);

        // Fail awaitable items; fire-and-forget items stay queued for reconnection
        var count = _sendQueue.Count;
        for (var i = 0; i < count; i++)
        {
            var item = _sendQueue.Dequeue();
            if (item.Tcs != null)
                item.Tcs.TrySetException(new InvalidOperationException("Connection lost"));
            else
                _sendQueue.Enqueue(item);
        }
    }

    private static void EnqueueSend(byte[] data, WebSocketMessageType type, TaskCompletionSource<bool>? tcs)
    {
        while (_sendQueue.Count >= MAX_SEND_QUEUE)
        {
            var oldest = _sendQueue.Dequeue();
            oldest.Tcs?.TrySetException(new InvalidOperationException("Send queue full"));
        }

        _sendQueue.Enqueue(new SendItem(data, type, tcs));
    }

    public static async Task DisconnectAsync()
    {
        if (!_isConnected && !_isConnecting)
            return;

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(DisconnectRoutine(tcs));
        await tcs.Task;
    }

    /// <summary>
    /// Synchronously sends a WebSocket close frame and disposes the socket.
    /// Use this during plugin unload where the coroutine scheduler may be torn down
    /// before an async disconnect coroutine gets a chance to run.
    /// </summary>
    public static void DisconnectSync()
    {
        if (!_isConnected && !_isConnecting)
            return;

        _isConnected = false;
        _isConnecting = false;

        StopSendLoop();
        ThoriumUnityScheduler.TryStopCoroutine(ref _receiveCoroutine);
        ThoriumUnityScheduler.TryStopCoroutine(ref _reconnectCoroutine);

        var ws = _webSocket;
        _webSocket = null;

        if (ws == null) return;

        try
        {
            if (ws.State == WebSocketState.Open)
                ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Plugin unloading", CancellationToken.None)
                    .Wait(3000);
        }
        catch { }
        finally
        {
            try { ws.Dispose(); } catch { }
        }
    }

    public static bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(token);

    public static void Reset()
    {
        try
        {
            _ = DisconnectAsync();
        }
        catch (Exception)
        {
            // intentionally ignored — best-effort disconnect on reset
        }
        DisposeWebSocket();

        OnMessageReceived = null;
        OnBinaryMessageReceived = null;
        OnConnected = null;
        OnDisconnected = null;
        _sendQueue.Clear();
        _isConnected = false;
        _isConnecting = false;
        _reconnectAttempts = 0;
        _currentUri = null;
        _sessionToken = null;
        _receiveCoroutine = null;
        _reconnectCoroutine = null;
        _sendLoopCoroutine = null;
        _serverInfo = null;
        _mapHash = string.Empty;
    }

    private static void InitializeWebSocket()
    {
        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        try
        {
            if (!string.IsNullOrWhiteSpace(_sessionToken))
            {
                _webSocket.Options.SetRequestHeader(SESSION_TOKEN_HEADER, _sessionToken);
            }

            _webSocket.Options.SetRequestHeader("X-Thorium-Version",
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0");
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"Failed to set session token header: {ex.Message}");
        }
    }

    private static IEnumerator AuthenticateRoutine(string hostname, TaskCompletionSource<bool> tcs)
    {
        Task<string>? authTask = null;
        Exception? authEx = null;

        try
        {
            var authUrl = $"https://{hostname}{AUTH_ENDPOINT}";

            Log.Debug(() => $"Authenticating with: {authUrl}");

            authTask = AuthenticateAsync(authUrl);
        }
        catch (Exception ex)
        {
            authEx = ex;
        }

        if (authEx != null || authTask == null)
        {
            Log.Error($"Failed to start authentication: {authEx?.Message ?? "Unknown error"}");
            tcs.TrySetException(
                new InvalidOperationException($"Failed to authenticate: {authEx?.Message ?? "Unknown error"}", authEx));
            yield break;
        }

        while (!authTask.IsCompleted)
            yield return null;

        if (authTask.IsFaulted)
        {
            var ex = authTask.Exception?.GetBaseException() ?? new InvalidOperationException("Authentication failed");
            Log.Error($"Authentication failed: {ex.Message}");
            tcs.TrySetException(new InvalidOperationException($"Failed to authenticate: {ex.Message}", ex));
            yield break;
        }

        _sessionToken = authTask.Result;
        Log.Debug(() => "Authentication successful");
        tcs.TrySetResult(true);
    }

    private static async Task<string> AuthenticateAsync(string authUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, authUrl);
        request.Headers.Add(SERVER_TOKEN_HEADER, token);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Authentication failed with status {response.StatusCode}: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        Log.Debug(() => $"Auth response: {content}");

        var authResponse = JsonConvert.DeserializeObject<AuthResponse>(content);

        if (authResponse == null)
            throw new InvalidOperationException($"Failed to deserialize authentication response. Body: {content}");

        if (string.IsNullOrWhiteSpace(authResponse.SessionToken))
            throw new InvalidOperationException(
                $"Authentication response did not contain a valid session token. Body: {content}");

        Log.Debug(() => 
            $"Received session token: {authResponse.SessionToken.Substring(0, Math.Min(10, authResponse.SessionToken.Length))}...");
        return authResponse.SessionToken;
    }

    private class AuthResponse
    {
        [JsonProperty("sessionToken")] public string SessionToken { get; set; } = string.Empty;
    }

    private static async Task SendLevelToBackendAsync()
    {
        Log.Debug(() => "SendLevelToBackendAsync started");
        var levelUrl = Server.levelurl;
        Log.Debug(() => $"Level URL: '{levelUrl}'");

        if (string.IsNullOrWhiteSpace(levelUrl))
        {
            Log.Debug(() => "Level URL empty, uploading map data from file...");
            await UploadMapDataAsync();
        }
        else
        {
            Log.Debug(() => $"Uploading level URL: {levelUrl}");
            await UploadLevelUrlAsync(levelUrl);
        }

        Log.Debug(() => $"SendLevelToBackendAsync completed. MapHash: '{_mapHash}'");
    }

    private static async Task UploadLevelUrlAsync(string levelUrl)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{_currentUri}/api/maps/levelurl");
            request.Headers.Add(SERVER_TOKEN_HEADER, token);
            var payload = new { levelUrl };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Log.Debug(() => $"Uploading level URL: {levelUrl}");
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"UploadLevelUrlAsync failed: {response.StatusCode}: {errorContent}");
                return;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Debug(() => $"Level URL upload response: {responseContent}");
            TrySetMapHashFromResponse(responseContent);
        }
        catch (Exception ex)
        {
            Log.Error($"UploadLevelUrlAsync exception: {ex.Message}");
        }
    }

    private static async Task UploadMapDataAsync()
    {
        try
        {
            var mapPath = Server.rootFolder + "/" + World.MapFileName;
            var size = Server.worldsize;
            var seed = Server.seed;

            if (!File.Exists(mapPath))
            {
                Log.Debug(() => $"Map file not found: {mapPath}");
                return;
            }

            Log.Debug(() => $"Uploading map data: {mapPath} (size: {size}, seed: {seed})");
            var mapData = await File.ReadAllBytesAsync(mapPath);

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://{_currentUri}/api/maps/upload?mapSize={size}&mapSeed={seed}");
            request.Headers.Add(SERVER_TOKEN_HEADER, token);
            request.Content = new ByteArrayContent(mapData);

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"UploadMapDataAsync failed: {response.StatusCode}: {errorContent}");
                return;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Debug(() => $"Map upload response: {responseContent}");
            TrySetMapHashFromResponse(responseContent);
        }
        catch (Exception ex)
        {
            Log.Error($"UploadMapDataAsync exception: {ex.Message}");
        }
    }

    private static void TrySetMapHashFromResponse(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent)) return;
        var mapResponse = DeserializeJson<MapResponse>(responseContent);
        if (mapResponse != null && !string.IsNullOrWhiteSpace(mapResponse.Hash))
        {
            _mapHash = mapResponse.Hash;
            Log.Debug(() => $"Map hash set to: {_mapHash}");
        }
    }

    private static IEnumerator ConnectRoutine(string hostname, TaskCompletionSource<bool> tcs)
    {
        var authTcs = new TaskCompletionSource<bool>();
        yield return AuthenticateRoutine(hostname, authTcs);

        if (authTcs.Task.IsFaulted)
        {
            _isConnecting = false;
            var authEx = authTcs.Task.Exception?.GetBaseException() ??
                         new InvalidOperationException("Authentication failed");
            Log.Error($"Failed to authenticate: {authEx.Message}");
            EnsureReconnectLoopRunning();
            tcs.TrySetException(authEx);
            yield break;
        }

        Log.Debug(() => "Starting SendLevelToBackendAsync...");
        Task? sendLevelTask = null;

        try
        {
            sendLevelTask = SendLevelToBackendAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"SendLevelToBackendAsync failed to start: {ex.Message}");
        }

        if (sendLevelTask != null)
        {
            while (!sendLevelTask.IsCompleted)
                yield return null;

            if (sendLevelTask.IsFaulted)
            {
                var ex = sendLevelTask.Exception?.GetBaseException();
                Log.Debug(() => $"SendLevelToBackendAsync faulted: {ex?.Message}");
            }
        }

        Task? connectTask = null;
        Exception? connectEx = null;

        try
        {
            DisposeWebSocket();
            InitializeWebSocket();

            if (_webSocket == null)
                throw new InvalidOperationException("ClientWebSocket initialization failed");

            var wsUri = $"wss://{hostname}{WS_ENDPOINT}";
            Log.Debug(() => $"Connecting to: {wsUri}");
            connectTask = _webSocket.ConnectAsync(new Uri(wsUri), CancellationToken.None);
        }
        catch (Exception ex)
        {
            connectEx = ex;
        }

        if (connectEx != null || connectTask == null)
        {
            _isConnecting = false;
            Log.Error($"Failed to connect: {connectEx?.Message ?? "Unknown error"}");
            tcs.TrySetException(
                new InvalidOperationException($"Failed to connect: {connectEx?.Message ?? "Unknown error"}",
                    connectEx));
            yield break;
        }

        while (!connectTask.IsCompleted)
            yield return null;

        if (connectTask.IsFaulted)
        {
            var ex = connectTask.Exception?.GetBaseException() ?? new InvalidOperationException("Connect failed");
            _isConnecting = false;
            Log.Error($"Failed to connect: {ex.Message}");
            EnsureReconnectLoopRunning();
            tcs.TrySetException(new InvalidOperationException($"Failed to connect: {ex.Message}", ex));
            yield break;
        }

        _isConnected = true;
        _isConnecting = false;
        _reconnectAttempts = 0;

        Log.Info("Connected to Thorium backend");
        OnConnected?.Invoke();

        ThoriumUnityScheduler.TryStopCoroutine(ref _reconnectCoroutine);

        StartSendLoop();

        if (_serverInfo != null)
        {
            Exception? sendEx = null;
            TaskCompletionSource<bool>? sendTcs = null;

            var resolvedIp = Server.ip;
            {
                var ipTask = _httpClient.GetStringAsync("https://api.ipify.org");
                while (!ipTask.IsCompleted)
                    yield return null;
                if (!ipTask.IsFaulted && !string.IsNullOrWhiteSpace(ipTask.Result))
                    resolvedIp = ipTask.Result.Trim();
            }

            try
            {
                _serverInfo.HostName = Server.hostname;
                _serverInfo.Port = Server.port;
                _serverInfo.IpAddress = resolvedIp;
                _serverInfo.MapHash = _mapHash;
                var serverInfoJson = JsonConvert.SerializeObject(_serverInfo);
                Log.Debug(() => $"Sending server info: {serverInfoJson}");
                sendTcs = new TaskCompletionSource<bool>();
                EnqueueSend(Encoding.UTF8.GetBytes(serverInfoJson), WebSocketMessageType.Text, sendTcs);
            }
            catch (Exception ex)
            {
                sendEx = ex;
            }

            if (sendTcs != null && sendEx == null)
            {
                while (!sendTcs.Task.IsCompleted)
                    yield return null;

                if (sendTcs.Task.IsFaulted)
                {
                    Log.Debug(() => $"Failed to send server info: {sendTcs.Task.Exception?.GetBaseException().Message}");
                }
            }
            else if (sendEx != null)
            {
                Log.Debug(() => $"Error sending server info: {sendEx.Message}");
            }
        }

        StartReceiveLoop();

        ThoriumUnityScheduler.RunCoroutine(SendInitialEntitiesRoutine());
        ThoriumUnityScheduler.RunCoroutine(SendInitialPlayerBaselineRoutine());

        tcs.TrySetResult(true);
    }

    private static IEnumerator SendInitialEntitiesRoutine()
    {
        const int maxSnapshotAttempts = 5;

        const int batchSize = 1000;
        const int sendThreshold = 5000;
        var entityCount = 0;
        var total = 0;
        var batchesSent = 0;

        Log.Debug(() => "Starting initial entity sync...");

        List<BaseEntity>? snapshot = null;
        for (var attempt = 1; attempt <= maxSnapshotAttempts; attempt++)
        {
            var realm = BaseNetworkable.serverEntities;
            if (realm == null)
            {
                var a1 = attempt;
                Log.Debug(() => $"Initial entity sync: serverEntities not ready (attempt {a1}/{maxSnapshotAttempts})");
                yield return null;
                continue;
            }

            try
            {
                snapshot = new List<BaseEntity>();
                foreach (var networkable in realm)
                {
                    if (networkable is BaseEntity entity)
                        snapshot.Add(entity);
                }

                if (snapshot.Count > 0)
                    break;

                snapshot = null;
                var a2 = attempt;
                Log.Debug(() => $"Initial entity sync: no entities found (attempt {a2}/{maxSnapshotAttempts})");
            }
            catch (Exception ex)
            {
                snapshot = null;
                var a3 = attempt;
                var msg = ex.Message;
                Log.Debug(() => $"Initial entity sync snapshot failed (attempt {a3}/{maxSnapshotAttempts}): {msg}");
            }

            yield return null;
        }

        if (snapshot == null || snapshot.Count == 0)
        {
            Log.Warning("Initial entity sync aborted: could not capture stable server entity snapshot");
            yield break;
        }

        Log.Debug(() => $"Initial entity snapshot captured: {snapshot.Count} entities");

        var localCache = new MemoryStream(1 << 24);
        long localEntityPackets = 0;
        long sentSoFar = 0;
        var totalExpected = snapshot.Count;

        try
        {
            foreach (var networkable in snapshot)
            {
                var entity = networkable;

                try
                {
                    var startPos = localCache.Position;
                    var ownerId = entity.OwnerID;
                    BinaryEventWriter.WriteBool(localCache, true);
                    BinaryEventWriter.WriteInt64(localCache, (long)entity.net.ID.Value);
                    BinaryEventWriter.WriteString(localCache, ownerId > 0 ? ownerId.ToString() : string.Empty);
                    BinaryEventWriter.WriteUint(localCache, entity.prefabID);
                    BinaryEventWriter.WriteString(localCache, entity.ShortPrefabName ?? string.Empty);
                    BinaryEventWriter.WriteVector(localCache, entity.ServerPosition);
                    BinaryEventWriter.WriteVector(localCache, entity.ServerRotation.eulerAngles);
                    BinaryEventWriter.WriteVector(localCache, entity.CenterPoint());
                    BinaryEventWriter.WriteVector(localCache, entity.bounds.extents);

                    localEntityPackets++;
                    total++;
                }
                catch (Exception)
                {
                    // skip individual entity write failures
                }

                if (++entityCount >= batchSize)
                {
                    entityCount = 0;

                    if (localEntityPackets >= sendThreshold)
                    {
                        var batchPackets = localEntityPackets;
                        var flush = FlushLocalEntityBatchRoutine(localCache, localEntityPackets);
                        while (flush.MoveNext()) yield return flush.Current;
                        batchesSent++;
                        sentSoFar += batchPackets;
                        var logSent = sentSoFar;
                        var logRemaining = Math.Max(0, totalExpected - sentSoFar);
                        Log.Debug(() => $"Entity Sync: {logSent} sent / {logRemaining} remaining");
                        localCache = new MemoryStream(1 << 24);
                        localEntityPackets = 0;
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }

            if (localEntityPackets > 0)
            {
                var batchPackets = localEntityPackets;
                var flush = FlushLocalEntityBatchRoutine(localCache, localEntityPackets);
                while (flush.MoveNext()) yield return flush.Current;
                batchesSent++;
                sentSoFar += batchPackets;
                var logSent2 = sentSoFar;
                var logRemaining2 = Math.Max(0, totalExpected - sentSoFar);
                Log.Debug(() => $"Entity Sync: {logSent2} sent / {logRemaining2} remaining");
            }

            var totalSent = total;
            var totalBatches = batchesSent;
            Log.Debug(() => $"Initial entity sync complete: {totalSent} entities in {totalBatches} batches");
        }
        finally
        {
            localCache.Dispose();
        }
    }

    private static IEnumerator FlushLocalEntityBatchRoutine(MemoryStream cache, long entityPackets)
    {
        int length;
        byte[] bytes;
        byte[] serialized;
        try
        {
            length = (int)cache.Length;
            if (length <= 0) yield break;

            bytes = new byte[length];
            if (cache.TryGetBuffer(out var seg))
                Array.Copy(seg.Array!, seg.Offset, bytes, 0, length);
            else
            {
                cache.Position = 0;
                _ = cache.Read(bytes, 0, length);
            }
            cache.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to prepare initial entity batch: {ex.Message}");
            yield break;
        }

        yield return null;

        try
        {
            var payload = new ThoriumEventPayload
            {
                EntityEventBytes = bytes,
                EntityEventLength = bytes.Length,
                EntityEventCount = entityPackets
            };

            var batch = new ThoriumBatch();
            serialized = ThoriumBatchProtobufSerializer.Serialize(batch, payload);

        }
        catch (Exception ex)
        {
            Log.Error($"Failed to serialize initial entity batch: {ex.Message}");
            yield break;
        }

        yield return null;

        SendBinaryFireAndForget(serialized);
    }

    private static IEnumerator SendInitialPlayerBaselineRoutine()
    {
        yield return null;

        var players = BasePlayer.activePlayerList;
        if (players == null || players.Count == 0)
        {
            Log.Debug(() => "Initial player baseline: no active players");
            yield break;
        }

        var count = 0;
        var skipped = 0;

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || player.IsDestroyed || player.net?.connection == null)
            {
                skipped++;
                continue;
            }

            try
            {
                var steamId = (long)player.userID;
                if (steamId <= 0) { skipped++; continue; }

                var pos = player.transform.position;
                var velocity = player.estimatedVelocity;
                var combat = CombatData.FromPlayer(player);

                var snapshot = PlayerSnapshot.Create(
                    pos, player, SnapshotTypeEnums.Baseline,
                    combat, velocity, player.IsOnGround());

                snapshot.WaterLevel = player.modelState.waterLevel;
                snapshot.WaterFactor = player.WaterFactor();
                snapshot.IsSwimming = player.IsSwimming();
                snapshot.IsDiving = player.IsHeadUnderwater();

                var eyes = player.eyes;
                if (eyes != null)
                {
                    snapshot.EyesPositionX = eyes.position.x;
                    snapshot.EyesPositionY = eyes.position.y;
                    snapshot.EyesPositionZ = eyes.position.z;
                }

                snapshot.ViewAnglesX = player.viewAngles.x;
                snapshot.ViewAnglesY = player.viewAngles.y;
                snapshot.ViewAnglesZ = player.viewAngles.z;

                var modelState = player.modelState;
                var lookDir = modelState.lookDir;
                snapshot.LookDirX = lookDir.x;
                snapshot.LookDirY = lookDir.y;
                snapshot.LookDirZ = lookDir.z;
                snapshot.PoseType = modelState.poseType;

                var inheritedVel = modelState.inheritedVelocity;
                snapshot.InheritedVelocityX = inheritedVel.x;
                snapshot.InheritedVelocityY = inheritedVel.y;
                snapshot.InheritedVelocityZ = inheritedVel.z;

                snapshot.EyesViewMode = (player.playerFlags & BasePlayer.PlayerFlags.EyesViewmode) != 0;
                snapshot.ThirdPersonViewMode = (player.playerFlags & BasePlayer.PlayerFlags.ThirdPersonViewmode) != 0;

                snapshot.ActiveItemId = player.GetActiveItem()?.uid.Value ?? 0;
                var parent = player.GetParentEntity();
                snapshot.ParentId = parent?.net?.ID.Value ?? 0;

                AntiCheatSnapshotProcessor.Enqueue(steamId, snapshot);
                count++;
            }
            catch
            {
                skipped++;
            }

            if (count > 0 && count % 50 == 0)
                yield return null;
        }

        var logCount = count;
        var logSkipped = skipped;
        Log.Debug(() => $"Initial player baseline complete: {logCount} players, {logSkipped} skipped");
    }

    private static void StartReceiveLoop()
    {
        ThoriumUnityScheduler.TryStopCoroutine(ref _receiveCoroutine);
        _receiveCoroutine = ThoriumUnityScheduler.RunCoroutine(ReceiveLoopRoutine());
    }

    private static IEnumerator SendLoopRoutine()
    {
        while (IsConnected)
        {
            if (_sendQueue.Count == 0)
            {
                yield return null;
                continue;
            }

            var item = _sendQueue.Dequeue();

            Task? sendTask = null;
            try
            {
                sendTask = _webSocket?.SendAsync(
                    new ArraySegment<byte>(item.Data), item.Type, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Debug(() => $"Send error: {ex.Message}");
                item.Tcs?.TrySetException(ex);
                HandleConnectionError();
                yield break;
            }

            if (sendTask == null)
            {
                item.Tcs?.TrySetException(new InvalidOperationException("WebSocket not available"));
                HandleConnectionError();
                yield break;
            }

            while (!sendTask.IsCompleted)
                yield return null;

            if (sendTask.IsFaulted)
            {
                var ex = sendTask.Exception?.GetBaseException() ?? new InvalidOperationException("Send failed");
                Log.Debug(() => $"Send failed: {ex.Message}");
                item.Tcs?.TrySetException(ex);
                HandleConnectionError();
                yield break;
            }

            item.Tcs?.TrySetResult(true);
        }

        _sendLoopCoroutine = null;
    }

    private static IEnumerator ReceiveLoopRoutine()
    {
        var buffer = new byte[BUFFER_SIZE];

        while (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            Task<WebSocketReceiveResult>? receiveTask = null;
            Exception? startReceiveEx = null;

            try
            {
                receiveTask = _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                startReceiveEx = ex;
            }

            if (startReceiveEx != null || receiveTask == null)
            {
                Log.Error($"Error starting receive: {startReceiveEx?.Message ?? "Unknown error"}");
                HandleConnectionError();
                yield break;
            }

            while (!receiveTask.IsCompleted)
                yield return null;

            if (receiveTask.IsFaulted)
            {
                var msg = receiveTask.Exception?.GetBaseException().Message ?? "Unknown error";
                Log.Error($"Error in receive loop: {msg}");
                HandleConnectionError();
                yield break;
            }

            var result = receiveTask.Result;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                OnMessageReceived?.Invoke(message);
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                // Skip heartbeat frames (single byte 0x01) sent by Gateway to keep Cloudflare alive
                if (result.Count == 1 && buffer[0] == 0x01)
                {
                    yield return null;
                    continue;
                }
                var data = new byte[result.Count];
                Array.Copy(buffer, data, result.Count);
                OnBinaryMessageReceived?.Invoke(data);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Log.Debug(() => "Server closed connection");
                // Send Close response before tearing down
                try
                {
                    if (_webSocket?.State == WebSocketState.CloseReceived)
                        _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(2000);
                }
                catch { }
                HandleConnectionError();
                yield break;
            }

            yield return null;
        }
    }

    private static void HandleConnectionError()
    {
        _isConnected = false;
        _isConnecting = false;

        StopSendLoop();
        ThoriumUnityScheduler.TryStopCoroutine(ref _receiveCoroutine);

        // Close the WebSocket gracefully so the Gateway doesn't get an unexpected EOF
        try
        {
            var ws = _webSocket;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnecting", CancellationToken.None)
                    .ContinueWith(_ => { try { ws.Dispose(); } catch { } });
            }
            else
            {
                try { ws?.Dispose(); } catch { }
            }
        }
        catch { }
        _webSocket = null;

        OnDisconnected?.Invoke();

        _reconnectAttempts = 0;
        EnsureReconnectLoopRunning();
    }

    private static IEnumerator ReconnectLoopRoutine()
    {
        while (true)
        {
            var delay = Math.Min(
                RECONNECT_INTERVAL_SECONDS * (1 << Math.Min(_reconnectAttempts, 4)),
                MAX_RECONNECT_INTERVAL_SECONDS);
            yield return new WaitForSecondsRealtime(delay);

            if (string.IsNullOrWhiteSpace(_currentUri))
                continue;

            if (IsConnected)
                yield break;

            if (_isConnecting)
                continue;

            _reconnectAttempts++;
            var attempt = _reconnectAttempts;
            Log.Debug(() => $"Reconnect attempt #{attempt} (delay was {delay}s)...");

            var tcs = new TaskCompletionSource<bool>();
            _isConnecting = true;
            yield return ConnectRoutine(_currentUri, tcs);

            if (tcs.Task.IsFaulted)
            {
                var ex = tcs.Task.Exception?.GetBaseException();
                Log.Debug(() => $"Reconnect failed: {ex?.Message ?? "Unknown error"}");
            }

            if (IsConnected)
                yield break;
        }
    }

    private static IEnumerator DisconnectRoutine(TaskCompletionSource<bool> tcs)
    {
        Log.Debug(() => "Disconnecting from server");

        _isConnected = false;
        _isConnecting = false;

        StopSendLoop();
        ThoriumUnityScheduler.TryStopCoroutine(ref _receiveCoroutine);
        ThoriumUnityScheduler.TryStopCoroutine(ref _reconnectCoroutine);

        Task? closeTask = null;
        Exception? closeEx = null;

        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                closeTask = _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                closeEx = ex;
            }
        }

        if (closeEx != null)
            Log.Debug(() => $"Error during disconnect: {closeEx.Message}");

        if (closeTask != null)
        {
            while (!closeTask.IsCompleted)
                yield return null;
        }

        OnDisconnected?.Invoke();
        DisposeWebSocket();
        tcs.TrySetResult(true);
    }

    private static void DisposeWebSocket()
    {
        try
        {
            _webSocket?.Dispose();
            _webSocket = null;
        }
        catch
        {
            _webSocket = null;
        }
    }

}
