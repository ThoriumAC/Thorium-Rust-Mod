using System;
using ThoriumRustMod.Config;

namespace ThoriumRustMod.Core;

public static class Log
{
    public static void Debug(string message)
    {
        if (ThoriumConfigService.DebugMode)
            UnityEngine.Debug.Log($"[Thorium] {message}");
    }

    public static void Debug(Func<string> messageFactory)
    {
        if (ThoriumConfigService.DebugMode)
            UnityEngine.Debug.Log($"[Thorium] {messageFactory()}");
    }

    public static void Info(string message)
    {
        UnityEngine.Debug.Log($"[Thorium] {message}");
    }

    public static void Warning(string message)
    {
        UnityEngine.Debug.LogWarning($"[Thorium] {message}");
    }

    public static void Error(string message)
    {
        UnityEngine.Debug.LogError($"[Thorium] {message}");
    }

    public static void Exception(Exception exception)
    {
        UnityEngine.Debug.LogError("[Thorium] Caught Exception: ");
        UnityEngine.Debug.LogException(exception);
    }
}