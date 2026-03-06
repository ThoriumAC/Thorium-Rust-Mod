using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ThoriumRustMod.Config;
using ThoriumRustMod.Core;

namespace ThoriumRustMod.Services;

internal static class ThoriumAutoUpdater
{
    private const string DOWNLOAD_BASE_URL = "https://dl.thorium.ac/api/v1/rust/download";
    private const string CHECK_BASE_URL = "https://dl.thorium.ac/api/v1/rust/check";
    private const string MOD_NAME = "Thorium";

    private static volatile bool _isUpdating;

    public static void Subscribe() => ThoriumClientService.OnMessageReceived += HandleMessage;
    public static void Unsubscribe() => ThoriumClientService.OnMessageReceived -= HandleMessage;

    private static string BuildCheckUrl()
    {
        var url = CHECK_BASE_URL;
        if (ThoriumConfigService.Develop)
            url += "?branch=development";
        else if (ThoriumConfigService.StagingVersion)
            url += "?branch=staging";
        return url;
    }

    private static string BuildDownloadUrl(string? version = null)
    {
        var name = Uri.EscapeDataString(Path.GetFileNameWithoutExtension(GetCurrentDllPath()));
        var url = $"{DOWNLOAD_BASE_URL}?name={name}";
        if (!string.IsNullOrEmpty(version))
            url += $"&version={Uri.EscapeDataString(version)}";
        if (ThoriumConfigService.Develop)
            url += "&branch=development";
        else if (ThoriumConfigService.StagingVersion)
            url += "&branch=staging";
        return url;
    }

    public static async Task<bool> CheckForUpdateOnStartupAsync()
    {
        if (_isUpdating) return false;
        try
        {
            Log.Info("[AutoUpdater] Checking for updates...");
            string json;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                var checkUrl = BuildCheckUrl();
                var httpResponse = await client.GetAsync(checkUrl);
                json = await httpResponse.Content.ReadAsStringAsync();
                if (!httpResponse.IsSuccessStatusCode)
                {
                    Log.Warning($"[AutoUpdater] Check endpoint returned {(int)httpResponse.StatusCode}: {json.Trim()}");
                    return false;
                }
            }

            var response = JsonConvert.DeserializeObject<CheckResponse>(json);
            if (response == null || string.IsNullOrEmpty(response.Version))
            {
                Log.Warning("[AutoUpdater] Invalid response from check endpoint");
                return false;
            }
            if (!IsValidVersion(response.Version))
            {
                Log.Warning("[AutoUpdater] Rejected startup check: invalid version in response");
                return false;
            }

            var currentVersion = typeof(ThoriumAutoUpdater).Assembly.GetName().Version?.ToString() ?? "";
            var remoteVersion = response.Version!.TrimStart('v');
            if (remoteVersion == currentVersion)
            {
                Log.Info($"[AutoUpdater] Already on latest version ({currentVersion})");
                return false;
            }

            Log.Info($"[AutoUpdater] Update available: {currentVersion} → {response.Version}");
            return await PerformUpdateAsync(response.Version);
        }
        catch (Exception ex)
        {
            Log.Warning($"[AutoUpdater] Startup update check failed: {ex.Message}");
            return false;
        }
    }

    public static void NotifyUpdateSuccess()
    {
        var currentPath = GetCurrentDllPath();
        var dllName = Path.GetFileNameWithoutExtension(currentPath);
        var oldPath = Path.Combine(Path.GetDirectoryName(currentPath)!, $"{dllName}_old.dll");
        try
        {
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            Log.Info("[AutoUpdater] Update applied successfully.");
        }
        catch (Exception ex)
        {
            Log.Warning($"[AutoUpdater] Failed to delete {dllName}_old.dll: {ex.Message}");
        }

        _ = NotifyBackendAsync("success", null);
    }

    public static void HandlePatchFailure(string failedPatch)
    {
        var currentPath = GetCurrentDllPath();
        var dllDir = Path.GetDirectoryName(currentPath)!;
        var dllName = Path.GetFileNameWithoutExtension(currentPath);
        var oldPath = Path.Combine(dllDir, $"{dllName}_old.dll");
        var failedPath = Path.Combine(dllDir, $"{dllName}_failed.dll");

        try
        {
            if (File.Exists(currentPath))
            {
                if (File.Exists(failedPath)) File.Delete(failedPath);
                File.Move(currentPath, failedPath);
            }

            if (File.Exists(oldPath))
            {
                File.Move(oldPath, currentPath);
                Log.Info($"[AutoUpdater] Patch failure on '{failedPatch}'. Restored previous version. Triggering rollback reload.");
                ConsoleSystem.Run(ConsoleSystem.Option.Server, $"harmony.load {dllName}");
                _ = NotifyBackendAsync("rolled_back", failedPatch);
            }
            else
            {
                Log.Error($"[AutoUpdater] Patch failure on '{failedPatch}'. No previous version to restore — nothing loaded until server restart.");
                _ = NotifyBackendAsync("critical", failedPatch);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoUpdater] Rollback failed: {ex.Message}. Server unprotected until restart.");
            _ = NotifyBackendAsync("critical", failedPatch);
        }
    }

    private static void HandleMessage(string json)
    {
        UpdateMessage? msg;
        try { msg = JsonConvert.DeserializeObject<UpdateMessage>(json); }
        catch { return; }

        if (msg?.Type != "update") return;
        if (string.IsNullOrEmpty(msg.Version) || string.IsNullOrEmpty(msg.Sha256)) return;

        if (!IsValidVersion(msg.Version))
        {
            Log.Warning("[AutoUpdater] Rejected update signal: invalid version format");
            return;
        }
        if (!IsValidSha256(msg.Sha256))
        {
            Log.Warning("[AutoUpdater] Rejected update signal: invalid sha256 format");
            return;
        }
        if (_isUpdating)
        {
            Log.Warning("[AutoUpdater] Update already in progress, ignoring duplicate signal");
            return;
        }

        Log.Info($"[AutoUpdater] Received update signal for version {msg.Version}");
        _ = PerformUpdateAsync(msg.Version, msg.Sha256.ToLowerInvariant());
    }

    private static async Task<bool> PerformUpdateAsync(string version, string? expectedSha256 = null)
    {
        _isUpdating = true;
        try
        {
            Log.Info($"[AutoUpdater] Downloading version {version}...");
            byte[] data;
            string verifiedHash;
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            {
                var httpResponse = await client.GetAsync(BuildDownloadUrl(version));
                if (!httpResponse.IsSuccessStatusCode)
                {
                    Log.Error($"[AutoUpdater] Download failed: HTTP {(int)httpResponse.StatusCode}");
                    _ = NotifyBackendAsync("update_failed", null);
                    _isUpdating = false;
                    return false;
                }

                if (!httpResponse.Headers.TryGetValues("X-Sha256", out var hashValues))
                {
                    Log.Error("[AutoUpdater] Server did not return X-Sha256 header");
                    _ = NotifyBackendAsync("update_failed", null);
                    _isUpdating = false;
                    return false;
                }

                verifiedHash = string.Join("", hashValues).ToLowerInvariant();
                if (!IsValidSha256(verifiedHash))
                {
                    Log.Error("[AutoUpdater] Invalid X-Sha256 header from server");
                    _ = NotifyBackendAsync("update_failed", null);
                    _isUpdating = false;
                    return false;
                }

                if (expectedSha256 != null && !verifiedHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error($"[AutoUpdater] SHA-256 mismatch. Signal: {expectedSha256}, Server: {verifiedHash}");
                    _ = NotifyBackendAsync("hash_mismatch", null);
                    _isUpdating = false;
                    return false;
                }

                data = await httpResponse.Content.ReadAsByteArrayAsync();
            }

            var actualHash = ComputeSha256Hex(data);
            if (!actualHash.Equals(verifiedHash, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error($"[AutoUpdater] SHA-256 mismatch on download body. Expected: {verifiedHash}, Got: {actualHash}");
                _ = NotifyBackendAsync("hash_mismatch", null);
                _isUpdating = false;
                return false;
            }

            return await ApplyUpdateAsync(data, verifiedHash);
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoUpdater] Update failed: {ex.Message}");
            _ = NotifyBackendAsync("update_failed", null);
            _isUpdating = false;
            return false;
        }
    }

    private static async Task<bool> ApplyUpdateAsync(byte[] data, string verifiedSha256)
    {
        _isUpdating = true;
        try
        {
            var currentPath = GetCurrentDllPath();
            var dllDir = Path.GetDirectoryName(currentPath)!;
            var dllName = Path.GetFileNameWithoutExtension(currentPath);
            var newPath = Path.Combine(dllDir, $"{dllName}_new.dll");
            var oldPath = Path.Combine(dllDir, $"{dllName}_old.dll");

            if (File.Exists(newPath)) File.Delete(newPath);
            await File.WriteAllBytesAsync(newPath, data);

            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(currentPath, oldPath);
            File.Move(newPath, currentPath);

            Log.Info($"[AutoUpdater] File swap complete (sha256: {verifiedSha256}). Triggering reload...");
            ThoriumUnityScheduler.RunCoroutine(ReloadCoroutine());
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[AutoUpdater] Apply failed: {ex.Message}");
            _ = NotifyBackendAsync("update_failed", null);
            _isUpdating = false;
            return false;
        }
    }

    private static System.Collections.IEnumerator ReloadCoroutine()
    {
        yield return null;
        var dllName = Path.GetFileNameWithoutExtension(GetCurrentDllPath());
        ConsoleSystem.Run(ConsoleSystem.Option.Server, $"harmony.load {dllName}");
    }

    private static async Task NotifyBackendAsync(string status, string? failedPatch)
    {
        try
        {
            await ThoriumClientService.SendJsonAsync(new { type = "update_result", status, failedPatch });
        }
        catch (Exception ex)
        {
            Log.Warning($"[AutoUpdater] Failed to notify backend of update result: {ex.Message}");
        }
    }

    private static string? _cachedDllPath;

    internal static string GetCurrentDllPath()
    {
        if (_cachedDllPath != null) return _cachedDllPath;
        try
        {
            var rustHarmony = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Rust.Harmony");
            if (rustHarmony == null)
            {
                Log.Debug(() => "[AutoUpdater] Rust.Harmony assembly not found in AppDomain");
                goto fallback;
            }

            var loaderType = rustHarmony.GetType("HarmonyLoader");
            if (loaderType == null)
            {
                Log.Debug(() => "[AutoUpdater] HarmonyLoader type not found");
                goto fallback;
            }

            var loadedModsField = loaderType.GetField("loadedMods",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (loadedModsField == null)
            {
                Log.Debug(() => "[AutoUpdater] loadedMods field not found");
                goto fallback;
            }

            if (loadedModsField.GetValue(null) is not System.Collections.IEnumerable mods)
            {
                Log.Debug(() => "[AutoUpdater] loadedMods value is not IEnumerable");
                goto fallback;
            }

            foreach (var mod in mods)
            {
                var modType = mod.GetType();
                var entryValue = modType.Name == "KeyValuePair`2"
                    ? modType.GetProperty("Value")?.GetValue(mod)
                    : mod;
                if (entryValue == null) continue;
                var entryType = entryValue.GetType();
                Log.Debug(() => $"[AutoUpdater] loadedMods entry type: {entryType.FullName}");

                var allFields = entryType.GetFields(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

                var asmField = System.Array.Find(allFields,
                    f => typeof(System.Reflection.Assembly).IsAssignableFrom(f.FieldType));
                var entryAsm = asmField?.GetValue(entryValue) as System.Reflection.Assembly;
                if (entryAsm == null)
                {
                    Log.Debug(() => $"[AutoUpdater] No Assembly-typed field found on {entryType.Name}");
                    continue;
                }

                bool isOurs;
                try { isOurs = entryAsm.GetType("ThoriumRustMod.Services.ThoriumAutoUpdater") != null; }
                catch { isOurs = false; }

                if (!isOurs) continue;

                var nameProp = entryType.GetProperty("Name",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (nameProp?.GetValue(entryValue) is string modName && !string.IsNullOrEmpty(modName))
                {
                    _cachedDllPath = Path.Combine(GetDllDirectoryFallback(), modName + ".dll");
                    Log.Debug(() => $"[AutoUpdater] Resolved DLL path via Name: {_cachedDllPath}");
                    return _cachedDllPath;
                }

                Log.Debug(() => $"[AutoUpdater] Name property null or missing on {entryType.Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(() => $"[AutoUpdater] GetCurrentDllPath reflection failed: {ex.Message}");
        }

        fallback:

        return Path.Combine(GetDllDirectoryFallback(), $"{MOD_NAME}.dll");
    }

    internal static string GetDllDirectory() =>
        Path.GetDirectoryName(GetCurrentDllPath())!;

    private static string GetDllDirectoryFallback()
    {
        try
        {
            var exeFile = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exeFile))
            {
                var exeDir = Path.GetDirectoryName(exeFile)!;
                var candidates = new[]
                {
                    Path.GetFullPath(Path.Combine(exeDir, "HarmonyMods")),
                    Path.GetFullPath(Path.Combine(exeDir, "RustDedicated_Data", "..", "HarmonyMods")),
                };
                foreach (var c in candidates)
                    if (Directory.Exists(c)) return c;
            }
        }
        catch { }

        var cwdCandidate = Path.Combine(Environment.CurrentDirectory, "HarmonyMods");
        if (Directory.Exists(cwdCandidate)) return cwdCandidate;

        return Environment.CurrentDirectory;
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }

    private static bool IsValidVersion(string version) =>
        version.Length <= 64 && Regex.IsMatch(version, @"^[a-zA-Z0-9.\-_]+$");

    private static bool IsValidSha256(string hash) =>
        hash.Length == 64 && Regex.IsMatch(hash, @"^[a-fA-F0-9]+$");

    private class CheckResponse
    {
        [JsonProperty("version")] public string? Version { get; set; }
    }

    private class UpdateMessage
    {
        [JsonProperty("type")] public string? Type { get; set; }
        [JsonProperty("version")] public string? Version { get; set; }
        [JsonProperty("sha256")] public string? Sha256 { get; set; }
    }
}
