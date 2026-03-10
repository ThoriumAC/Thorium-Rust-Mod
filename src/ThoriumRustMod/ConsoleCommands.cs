using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using ThoriumRustMod.Config;
using ThoriumRustMod.Core;
using ThoriumRustMod.Services;
using UnityEngine;

namespace ThoriumRustMod;

public static class ConsoleCommands
{
    private const string COMMAND_PREFIX = "thorium";

    public static void RegisterCommands()
    {
        try
        {
            if (ConsoleSystem.Index.Server.Dict == null) return;
            RegisterCommand("status", "Displays the status of the Thorium mod.", true, true, true, ExecuteStatusCommand);
            RegisterCommand("version", "Displays the Thorium mod version.", true, true, true, ExecuteVersionCommand);
            RegisterCommand("setup", "Sets up the Thorium server token. Usage: thorium.setup <token>", false, true, false, ExecuteSetupCommand);
            RegisterCommand("debug", "Toggles debug logging. Usage: thorium.debug [true|false]", false, true, false, ExecuteDebugCommand);
        }
        catch
        {
        }
    }

    public static void Reset()
    {
        try
        {
            if (ConsoleSystem.Index.Server.Dict == null) return;
            UnregisterCommand("status");
            UnregisterCommand("version");
            UnregisterCommand("setup");
            UnregisterCommand("debug");
        }
        catch
        {
        }
    }

    private static void RegisterCommand(string name, string description,
        bool serverUser, bool serverAdmin, bool clientAdmin,
        Action<ConsoleSystem.Arg> callback)
    {
        var fullName = $"{COMMAND_PREFIX}.{name}";
        var newCommand = new ConsoleSystem.Command
        {
            Name = name,
            FullName = fullName,
            Description = description,
            Parent = COMMAND_PREFIX,
            AllowRunFromServer = true,
            ServerUser = serverUser,
            ServerAdmin = serverAdmin,
            ClientAdmin = clientAdmin,
            Call = callback
        };

        ConsoleSystem.Index.Server.Dict[fullName] = newCommand;
        ConsoleSystem.Index.All = ConsoleSystem.Index.All.Where(v => v.FullName != fullName).Append(newCommand).ToArray();
    }

    private static void UnregisterCommand(string name)
    {
        var fullName = $"{COMMAND_PREFIX}.{name}";

        ConsoleSystem.Index.Server.Dict.Remove(fullName);
        ConsoleSystem.Index.All = ConsoleSystem.Index.All.Where(v => v.FullName != fullName).ToArray();
    }

    private static void ExecuteDebugCommand(ConsoleSystem.Arg arg)
    {
        try
        {
            if (arg.Connection != null)
            {
                arg.ReplyWith("This command can only be run from the server console.");
                return;
            }

            var valueStr = arg.GetString(0);

            if (string.IsNullOrWhiteSpace(valueStr))
            {
                arg.ReplyWith($"Debug mode: {(ThoriumConfigService.DebugMode ? "enabled" : "disabled")}");
                return;
            }

            var enabled = valueStr.ToLowerInvariant() == "true" || valueStr == "1";
            ThoriumConfigService.SetDebugMode(enabled);
            arg.ReplyWith($"Debug mode {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            arg.ReplyWith($"Error: {ex.Message}");
        }
    }

    private static void ExecuteSetupCommand(ConsoleSystem.Arg arg)
    {
        try
        {
            if (arg.Connection != null)
            {
                arg.ReplyWith("This command can only be run from the server console.");
                return;
            }

            var token = arg.GetString(0);
            if (string.IsNullOrWhiteSpace(token))
            {
                arg.ReplyWith("Usage: thorium.setup <token>");
                return;
            }

            if (!ThoriumConfigService.SetServerToken(token))
            {
                arg.ReplyWith("Failed to save server token.");
                return;
            }

            arg.ReplyWith("Server token saved. Connecting to backend...");
            ThoriumUnityScheduler.RunCoroutine(ConnectAfterSetupRoutine());
        }
        catch (Exception ex)
        {
            arg.ReplyWith($"Error: {ex.Message}");
        }
    }

    private static IEnumerator ConnectAfterSetupRoutine()
    {
        Task connectTask = null;
        try
        {
            connectTask = ThoriumClientService.ConnectAsync(ThoriumLoader.BACKEND_URI);
        }
        catch
        {
        }

        if (connectTask == null)
        {
            ThoriumClientService.EnsureReconnectLoopRunning();
            yield break;
        }

        var startTime = Time.realtimeSinceStartup;
        while (!connectTask.IsCompleted)
        {
            if (Time.realtimeSinceStartup - startTime >= 10f)
            {
                ThoriumClientService.EnsureReconnectLoopRunning();
                yield break;
            }

            yield return null;
        }

        if (connectTask.IsFaulted)
            ThoriumClientService.EnsureReconnectLoopRunning();
    }

    private static void ExecuteVersionCommand(ConsoleSystem.Arg arg)
    {
        arg.ReplyWith($"Thorium v{ThoriumLoader.Version}");
    }

    private static void ExecuteStatusCommand(ConsoleSystem.Arg arg)
    {
        try
        {
            var tokenStatus = ThoriumConfigService.HasValidToken ? "Configured" : "Not configured";
            var tokenPreview = ThoriumConfigService.HasValidToken &&
                               ThoriumConfigService.ServerToken != null
                ? $"{ThoriumConfigService.ServerToken.Substring(0, Math.Min(10, ThoriumConfigService.ServerToken.Length))}..."
                : "N/A";

            arg.ReplyWith($"Thorium Status (v{ThoriumLoader.Version}):\n" +
                          $"- Token: {tokenStatus} ({tokenPreview})\n" +
                          $"- Connected: {ThoriumClientService.IsConnected}\n" +
                          $"- Debug: {(ThoriumConfigService.DebugMode ? "enabled" : "disabled")}\n" +
                          $"- Worker: {(AntiCheatSnapshotProcessor.IsWorkerRunning ? "Running" : "Stopped")}\n" +
                          $"- Buffer: {AntiCheatSnapshotProcessor.BufferCount} players");
        }
        catch (Exception ex)
        {
            arg.ReplyWith($"Error: {ex.Message}");
        }
    }
}