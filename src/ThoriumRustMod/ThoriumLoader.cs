using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConVar;
using ThoriumRustMod.Config;
using ThoriumRustMod.Core;
using ThoriumRustMod.Services;
using Time = UnityEngine.Time;

namespace ThoriumRustMod;

public class ThoriumLoader : IHarmonyModHooks
{
    public const string BACKEND_URI = "gateway.thorium.ac";
    public const string BACKEND_URI_DEV = "gateway-dev.thorium.ac";
    private const int CONNECTION_TIMEOUT_MS = 5000;

    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    public static bool __serverStarted;
    public static string MAP_HASH = "";
    public static Dictionary<uint, Action<BasePlayer, BaseEntity?>> RpcInterceptors = new();

    private static bool _isOldBuild;
    private static HarmonyLib.Harmony? _harmonyInstance;

    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        try
        {
            Log.Info($"Thorium v{Version} loading...");
            InitializeOnMainThread();

            _isOldBuild = ThoriumAutoUpdater.GetCurrentDllPath()
                .EndsWith("_old.dll", StringComparison.OrdinalIgnoreCase);

            if (!_isOldBuild && ThoriumConfigService.AutoUpdateOnStart)
            {
                var updateCheckTask = ThoriumAutoUpdater.CheckForUpdateOnStartupAsync();
                ThoriumUnityScheduler.RunCoroutine(StartupAfterUpdateCheckRoutine(updateCheckTask));
            }
            else
            {
                ThoriumUnityScheduler.RunCoroutine(PatchAndStartRoutine());
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Fatal error during mod loading: {ex.Message}");
        }
    }

    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        try
        {
            CleanupResources();
            Log.Info("ThoriumLoader unloaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error during mod unloading: {ex.Message}");
        }
    }

    public static void OnServerStarted()
    {
        if (__serverStarted)
            return;

        __serverStarted = true;
        Log.Info("Server started - beginning post-initialization");
        ThoriumUnityScheduler.RunCoroutine(ServerStartupRoutine());
    }

    private void InitializeOnMainThread()
    {
        ThoriumUnityScheduler.EnsureInitialized();
        ThoriumConfigService.Initialize();
    }

    private static void RegisterUnhandledExceptionHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Unhandled exception: {e.ExceptionObject}");
    }

    private static IEnumerator StartupAfterUpdateCheckRoutine(Task<bool> updateCheckTask)
    {
        while (!updateCheckTask.IsCompleted)
            yield return null;

        if (!updateCheckTask.IsFaulted && updateCheckTask.Result)
            yield break;

        yield return PatchAndStartRoutine();
    }

    private static IEnumerator PatchAndStartRoutine()
    {
        _harmonyInstance = new HarmonyLib.Harmony("com.thorium.manual");
        if (!ThoriumPatchRegistry.ApplyAll(_harmonyInstance))
        {
            ThoriumPatchRegistry.UnpatchAll();
            ThoriumAutoUpdater.HandlePatchFailure(ThoriumPatchRegistry.LastFailedPatch ?? "unknown");
            yield break;
        }

        RegisterUnhandledExceptionHandler();

        if (!_isOldBuild)
        {
            var currentPath = ThoriumAutoUpdater.GetCurrentDllPath();
            var dllName = System.IO.Path.GetFileNameWithoutExtension(currentPath);
            var oldDll = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentPath)!, $"{dllName}_old.dll");
            if (File.Exists(oldDll))
                ThoriumAutoUpdater.NotifyUpdateSuccess();
        }

        ThoriumUnityScheduler.RunCoroutine(StartWhenServerReadyRoutine());
    }

    private static IEnumerator StartWhenServerReadyRoutine()
    {
        if (__serverStarted)
            yield break;

        while (ServerMgr.Instance == null)
            yield return null;

        yield return null;
        OnServerStarted();
    }

    private static void SetupServerInfo()
    {
        try
        {
            var serverInfo = new Models.ServerInfo
            {
                HostName = Server.hostname ?? "Unknown Server",
                MapHash = MAP_HASH,
                IpAddress = Server.ip ?? "0.0.0.0",
                Port = Server.port
            };
            ThoriumClientService.SetServerInfo(serverInfo);
            Log.Debug(() => $"Server info configured: {serverInfo.HostName} ({serverInfo.IpAddress}:{serverInfo.Port})");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to setup server info: {ex.Message}");
        }
    }

    private static IEnumerator ConnectToBackendRoutine()
    {
        if (!ThoriumClientService.IsConfigured)
        {
            Log.Info("No server token configured. Run 'thorium.setup <token>' to enable.");
            yield break;
        }

        Task connectTask;
        try
        {
            connectTask = ThoriumClientService.ConnectAsync(
                ThoriumConfigService.Develop ? BACKEND_URI_DEV : BACKEND_URI);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to start backend connect: {ex.Message}");
            ThoriumClientService.EnsureReconnectLoopRunning();
            yield break;
        }

        var startTime = Time.realtimeSinceStartup;
        while (!connectTask.IsCompleted)
        {
            if ((Time.realtimeSinceStartup - startTime) * 1000f >= CONNECTION_TIMEOUT_MS)
            {
                Log.Warning("Backend connect timed out; retrying in background");
                ThoriumClientService.EnsureReconnectLoopRunning();
                yield break;
            }
            yield return null;
        }

        if (connectTask.IsFaulted)
        {
            var msg = connectTask.Exception?.GetBaseException().Message ?? "Unknown error";
            Log.Warning($"Failed to connect: {msg}. Retrying in background");
            ThoriumClientService.EnsureReconnectLoopRunning();
        }
    }

    private static IEnumerator ServerStartupRoutine()
    {
        SetupServerInfo();
        yield return ConnectToBackendRoutine();

        try
        {
            AntiCheatSnapshotProcessor.StartWorker();
            RegisterConsoleCommands();
            if (!_isOldBuild && ThoriumConfigService.AutoUpdateOnRunning)
                ThoriumAutoUpdater.Subscribe();
            Log.Info("Thorium initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Critical error during startup: {ex.Message}");
            HandleCriticalError();
        }
    }

    private static void RegisterConsoleCommands()
    {
        try
        {
            ConsoleCommands.RegisterCommands();
        }
        catch (Exception ex)
        {
            Log.Warning($"Console command registration failed: {ex.Message}\n{ex}");
        }
    }

    private static void HandleCriticalError()
    {
        try
        {
            HarmonyLoader.TryUnloadMod(typeof(ThoriumLoader).Assembly.GetName().Name);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to unload mod after critical error: {ex.Message}");
        }
    }

    private static void CleanupResources()
    {
        __serverStarted = false;

        ThoriumAutoUpdater.Unsubscribe();
        ThoriumPatchRegistry.UnpatchAll();

        try { AntiCheatSnapshotProcessor.StopWorker(); }
        catch (Exception ex) { Log.Warning($"Error stopping snapshot processor: {ex.Message}\n{ex}"); }

        try { ThoriumClientService.DisconnectSync(); }
        catch (Exception ex) { Log.Warning($"Error disconnecting backend client: {ex.Message}\n{ex}"); }

        try { ThoriumClientService.Reset(); } catch { }
        try { AntiCheatSnapshotProcessor.Reset(); } catch { }
        ConsoleCommands.Reset();
        try { DataHandler.Reset(); } catch { }
        ThoriumConfigService.Reset();

        try { ThoriumUnityScheduler.DestroyInstance(); }
        catch (Exception ex) { Log.Warning($"Error destroying Unity scheduler: {ex.Message}\n{ex}"); }
    }
}