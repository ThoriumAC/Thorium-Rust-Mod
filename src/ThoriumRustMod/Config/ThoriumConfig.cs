using System;
using System.IO;
using ConVar;
using UnityEngine;

namespace ThoriumRustMod.Config;

public class ThoriumConfig
{
    public string ServerToken { get; set; }
    public bool Debug { get; set; }
    public bool AutoUpdateOnStart { get; set; } = true;
    public bool AutoUpdateOnRunning { get; set; } = true;
    public bool StagingVersion { get; set; } = false;
    public bool Develop { get; set; } = false;
}

public static class ThoriumConfigService
{
    private const string CONFIG_FOLDER = "../../.thorium";
    private const string CONFIG_FILE = "thorium.yml";

    private static ThoriumConfig _config = new();
    private static string _configPath = string.Empty;
    private static bool _isLoaded;

    public static ThoriumConfig Config => _config;
    public static bool IsLoaded => _isLoaded;
    public static bool HasValidToken => !string.IsNullOrWhiteSpace(_config.ServerToken);
    public static string ServerToken => _config.ServerToken;
    public static bool DebugMode => _config.Debug;
    public static bool AutoUpdateOnStart => _config.AutoUpdateOnStart;
    public static bool AutoUpdateOnRunning => _config.AutoUpdateOnRunning;
    public static bool StagingVersion => _config.StagingVersion;
    public static bool Develop => _config.Develop;

    public static void Initialize()
    {
        InitializeConfigPath();
        LoadConfig();
    }

    public static void Reset()
    {
        _config = new ThoriumConfig();
        _configPath = string.Empty;
        _isLoaded = false;
    }

    public static bool SetServerToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        _config.ServerToken = token.Trim();
        return SaveConfig();
    }

    public static bool SetDebugMode(bool enabled)
    {
        _config.Debug = enabled;
        return SaveConfig();
    }

    public static void ReloadConfig() => LoadConfig();

    private static void InitializeConfigPath()
    {
        try
        {
            var serverRoot = GetServerRootPath();
            var configFolder = Path.Combine(serverRoot, CONFIG_FOLDER);
            _configPath = Path.Combine(configFolder, CONFIG_FILE);
        }
        catch
        {
            _configPath = Path.Combine(CONFIG_FOLDER, CONFIG_FILE);
        }
    }

    private static string GetServerRootPath()
    {
        try
        {
            var rootFolder = Server.rootFolder;
            return !string.IsNullOrEmpty(rootFolder) ? rootFolder : Environment.CurrentDirectory;
        }
        catch
        {
            return ".";
        }
    }

    private static void LoadConfig()
    {
        _isLoaded = false;
        _config = new ThoriumConfig();

        try
        {
            if (!File.Exists(_configPath)) return;
            var content = File.ReadAllText(_configPath);
            ParseYaml(content);
            _isLoaded = true;
            MigrateConfigIfNeeded(content);
        }
        catch { }
    }

    // Writes back any missing keys so new fields added in later versions get their defaults persisted
    private static void MigrateConfigIfNeeded(string existingContent)
    {
        var lower = existingContent.ToLowerInvariant();
        var needsMigration =
            (!lower.Contains("autoupdateonstart") && !lower.Contains("auto_update_on_start")) ||
            (!lower.Contains("autoupdateonrunning") && !lower.Contains("auto_update_on_running")) ||
            (!lower.Contains("stagingversion") && !lower.Contains("staging_version"));

        if (needsMigration)
            SaveConfig();
    }

    private static bool SaveConfig()
    {
        try
        {
            var configDir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            var content = $"ServerToken: \"{_config.ServerToken ?? ""}\"\nDebug: {_config.Debug.ToString().ToLowerInvariant()}\nAutoUpdateOnStart: {_config.AutoUpdateOnStart.ToString().ToLowerInvariant()}\nAutoUpdateOnRunning: {_config.AutoUpdateOnRunning.ToString().ToLowerInvariant()}\nStagingVersion: {_config.StagingVersion.ToString().ToLowerInvariant()}";
            File.WriteAllText(_configPath, content);
            _isLoaded = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ParseYaml(string content)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed[0] == '#') continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = trimmed.Substring(0, colonIndex).Trim().ToLowerInvariant();
            var value = trimmed.Substring(colonIndex + 1).Trim();

            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') ||
                                       (value[0] == '\'' && value[value.Length - 1] == '\'')))
                value = value.Substring(1, value.Length - 2);

            switch (key)
            {
                case "servertoken":
                case "server_token":
                    _config.ServerToken = value;
                    break;
                case "debug":
                    _config.Debug = value.ToLowerInvariant() == "true" || value == "1";
                    break;
                case "autoupdateonstart":
                case "auto_update_on_start":
                    _config.AutoUpdateOnStart = value.ToLowerInvariant() != "false" && value != "0";
                    break;
                case "autoupdateonrunning":
                case "auto_update_on_running":
                    _config.AutoUpdateOnRunning = value.ToLowerInvariant() != "false" && value != "0";
                    break;
                case "stagingversion":
                case "staging_version":
                    _config.StagingVersion = value.ToLowerInvariant() == "true" || value == "1";
                    break;
                case "develop":
                    _config.Develop = value.ToLowerInvariant() == "true" || value == "1";
                    break;
            }
        }
    }

    public static void Log(string message)
    {
        if (_isLoaded && _config.Debug)
            Debug.Log(message);
    }

    public static void LogAlways(string message) => Debug.Log(message);
    public static void LogError(string message) => Debug.LogError(message);
    public static void LogWarning(string message) => Debug.LogWarning(message);
}

