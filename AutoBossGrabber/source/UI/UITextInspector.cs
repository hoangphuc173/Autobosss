using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BepInEx.Logging;

namespace AutoBossGrabber;

/// <summary>
/// Inspector chi tiết cho UI Text components - tìm đúng nguồn boss notification.
/// Dump metadata đầy đủ: hierarchy path, canvas, position, timing.
/// </summary>
public static class UITextInspector
{
    private static ManualLogSource _log;
    private static HashSet<string> _seenTexts = new HashSet<string>();
    private static float _lastDumpTime = 0f;

    public static void Initialize(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>
    /// Dump chi tiết TẤT CẢ Text components đang active.
    /// Gọi khi KHÔNG có boss notification để có baseline.
    /// </summary>
    public static void DumpAllActiveTexts(string filename)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== UI TEXT INSPECTOR - FULL DUMP ===");
        sb.AppendLine($"Time: {DateTime.Now}");
        sb.AppendLine($"Frame: {Time.frameCount}");
        sb.AppendLine();

        int count = 0;

        // UI.Text
        var uiTexts = UnityEngine.Object.FindObjectsOfType<Text>();
        sb.AppendLine($"--- UI.Text Components: {uiTexts.Length} ---");
        foreach (var text in uiTexts)
        {
            if (text == null) continue;
            DumpTextComponent(sb, text.gameObject, text.text, "UI.Text", text.gameObject.activeInHierarchy);
            count++;
        }

        sb.AppendLine();

        // TMPro
        var tmpTexts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
        sb.AppendLine($"--- TextMeshProUGUI Components: {tmpTexts.Length} ---");
        foreach (var text in tmpTexts)
        {
            if (text == null) continue;
            DumpTextComponent(sb, text.gameObject, text.text, "TMP", text.gameObject.activeInHierarchy);
            count++;
        }

        sb.AppendLine();
        sb.AppendLine($"=== TOTAL: {count} components ===");

        // Save to file
        SaveDump(filename, sb.ToString());
    }

    /// <summary>
    /// Dump ONLY các text components mới xuất hiện hoặc thay đổi nội dung.
    /// Gọi liên tục để catch boss notification ngay khi nó xuất hiện.
    /// </summary>
    public static void MonitorNewTexts()
    {
        // Throttle: chỉ log 1 lần / 0.5s
        if (Time.time - _lastDumpTime < 0.5f)
            return;

        _lastDumpTime = Time.time;

        try
        {
            // Check UI.Text
            var uiTexts = UnityEngine.Object.FindObjectsOfType<Text>();
            foreach (var text in uiTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy)
                    continue;

                string key = $"{text.GetHashCode()}:{text.text}";
                if (_seenTexts.Contains(key))
                    continue;

                _seenTexts.Add(key);

                // Log new text with hierarchy
                var path = GetTransformPath(text.transform, 6);
                _log?.LogInfo($"[UIInspector] NEW UI.Text: path='{path}' text='{text.text}'");
            }

            // Check TMPro
            var tmpTexts = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>();
            foreach (var text in tmpTexts)
            {
                if (text == null || !text.gameObject.activeInHierarchy)
                    continue;

                string key = $"{text.GetHashCode()}:{text.text}";
                if (_seenTexts.Contains(key))
                    continue;

                _seenTexts.Add(key);

                // Log new text with hierarchy
                var path = GetTransformPath(text.transform, 6);
                _log?.LogInfo($"[UIInspector] NEW TMP: path='{path}' text='{text.text}'");
            }

            // Cleanup old entries
            if (_seenTexts.Count > 200)
                _seenTexts.Clear();
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[UIInspector] MonitorNewTexts error: {ex.Message}");
        }
    }

    private static void DumpTextComponent(StringBuilder sb, GameObject go, string text, string type, bool active)
    {
        var path = GetTransformPath(go.transform, 6);
        var canvas = go.GetComponentInParent<Canvas>();
        var canvasName = canvas != null ? canvas.name : "NoCanvas";
        var position = go.transform.position;

        sb.AppendLine($"[{(active ? "ACTIVE" : "hidden")}] {type}");
        sb.AppendLine($"  Path: {path}");
        sb.AppendLine($"  GameObject: {go.name}");
        sb.AppendLine($"  Canvas: {canvasName}");
        sb.AppendLine($"  Position: ({position.x:F1}, {position.y:F1}, {position.z:F1})");
        sb.AppendLine($"  Text: '{text}'");
        sb.AppendLine();
    }

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

    private static void SaveDump(string filename, string content)
    {
        try
        {
            var asmDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var dumpDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(asmDir, "..", "..", "BepInEx", "dump"));
            System.IO.Directory.CreateDirectory(dumpDir);
            var path = System.IO.Path.Combine(dumpDir, filename);
            System.IO.File.WriteAllText(path, content);
            _log?.LogInfo($"[UIInspector] Saved to: {path}");
        }
        catch (Exception ex)
        {
            _log?.LogError($"[UIInspector] Failed to save dump: {ex.Message}");
        }
    }
}

