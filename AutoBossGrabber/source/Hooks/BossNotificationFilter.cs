using System;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Logging;

namespace AutoBossGrabber;

/// <summary>
/// Filter UI Text components by hierarchy to exclude chat scroll and other false sources.
/// Prevents reading old boss notifications from chat history.
/// </summary>
public static class BossNotificationFilter
{
    private static ManualLogSource _log;

    public static void Initialize(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>
    /// Main entry point - returns true if text is from valid notification source.
    /// Only accept SystemChatObject (real popup), reject ChatPanel chat scroll.
    /// </summary>
    public static bool IsValidNotificationSource(Component textComponent)
    {
        if (textComponent == null)
            return false;

        var path = GetTransformPath(textComponent.transform, 5);

        // Log ALL text components for debugging (throttled)
        bool isSystemChat = IsSystemChatObject(path, textComponent.transform);

        // Log what we're filtering
        if (!isSystemChat)
        {
            _log?.LogDebug($"[Filter] REJECTED: path='{path}'");
        }
        else
        {
            _log?.LogInfo($"[Filter] ACCEPTED SystemChatObject: path='{path}'");
        }

        return isSystemChat;
    }

    /// <summary>
    /// Detect real notification: SystemChatObject(Clone) in path.
    /// Evidence from BepInEx/dump/ui_text_inspector_073230.txt:9
    /// Path: HUDCanvas/SystemChatObject(Clone)/Mask/Text
    /// Text: '<color=#F1AD0B>(HT): </color> Vua Vegiita Ảo Ảnh vừa xuất hiện...'
    /// </summary>
    private static bool IsSystemChatObject(string path, Transform t)
    {
        // Pattern 1: Path contains SystemChatObject
        if (path.Contains("SystemChatObject"))
        {
            return true;
        }

        // Pattern 2: Check ancestors for SystemChatObject name
        var current = t;
        for (int i = 0; i < 5 && current != null; i++)
        {
            if (current.name.Contains("SystemChatObject"))
                return true;
            current = current.parent;
        }

        return false;
    }

    /// <summary>
    /// Old blacklist method - no longer used.
    /// </summary>
    private static bool IsInChatScroll(string path, Transform t)
    {
        // Pattern 1: GameObject named "ChatObject" with parent "Content"
        if (t.gameObject.name == "ChatObject" &&
            t.parent != null && t.parent.name == "Content")
        {
            return true;
        }

        // Pattern 2: Path contains chat scroll keywords
        if (path.Contains("/Content/ChatObject") ||
            path.Contains("/ChatPanel/Content/") ||
            path.Contains("/ScrollView/Content/ChatObject"))
        {
            return true;
        }

        // Pattern 3: Check if any ancestor is named ChatObject in a Content parent
        var current = t;
        for (int i = 0; i < 4 && current != null; i++)
        {
            if (current.name == "ChatObject" && current.parent != null && current.parent.name == "Content")
                return true;
            current = current.parent;
        }

        return false;
    }

    /// <summary>
    /// Helper: build parent hierarchy path for debugging.
    /// Example: "HUDCanvas/ChatPanel/Content/ChatObject"
    /// </summary>
    private static string GetTransformPath(Transform t, int maxDepth)
    {
        var parts = new List<string>();
        var current = t;
        int depth = 0;

        while (current != null && depth < maxDepth)
        {
            parts.Insert(0, current.name);
            current = current.parent;
            depth++;
        }

        return string.Join("/", parts);
    }
}
