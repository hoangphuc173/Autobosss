using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AutoBossGrabber;

/// <summary>
/// Dump toàn bộ UI panels + class runtime ra file.
/// Pattern từ NpcScanner.UiPanelDumper:
///   - Resources.FindObjectsOfTypeAll<T>() (catch cả ẩn)
///   - Object.FindObjectsOfType<T>() (chỉ active)
///   - Scan method/field của class được dump
///
/// Output: <game_root>/BepInEx/ui_panel_dump_*.txt + runtime_types_*.txt
/// </summary>
public static class UiPanelDumper
{
    private static readonly string[] RELEVANT_KEYWORDS = new[]
    {
        "teleport", "zone", "map", "change", "panel", "ui", "menu",
        "dich", "khu", "return", "back", "quay", "capsule", "inventory",
        "shop", "skill", "quest", "boss", "die", "death"
    };

    public static void DumpAll(string filename = "ui_panel_dump.txt")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== UI PANEL DUMP {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine();

        try
        {
            // 1) Active MonoBehaviour
            var monos = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            sb.AppendLine($"--- Active MonoBehaviour ({monos.Length}) ---");
            var grouped = new Dictionary<string, List<MonoBehaviour>>();
            foreach (var m in monos)
            {
                if (m == null) continue;
                var t = m.GetType();
                var name = t.Name;
                if (!grouped.ContainsKey(name)) grouped[name] = new List<MonoBehaviour>();
                grouped[name].Add(m);
            }
            foreach (var kv in grouped.OrderByDescending(x => x.Value.Count).Take(100))
            {
                sb.AppendLine($"  {kv.Key}: {kv.Value.Count}");
                if (IsRelevant(kv.Key))
                {
                    sb.AppendLine("    *** RELEVANT - DUMPING FIELDS/METHODS ***");
                    var first = kv.Value.FirstOrDefault();
                    if (first != null) DumpInstance(sb, first);
                }
            }
            sb.AppendLine();

            // 2) ALL UI Buttons (kể cả inactive/hidden) - dùng FindObjectsOfTypeAll
            var allBtnObjects = Il2CppAPI.FindObjectsOfTypeAll(typeof(UnityEngine.UI.Button));
            sb.AppendLine($"--- ALL UI Buttons (active + inactive) ({allBtnObjects.Length}) ---");
            int btnIdx = 0;
            foreach (var obj in allBtnObjects)
            {
                var btn = obj as UnityEngine.UI.Button;
                if (btn == null) continue;
                string txt = GetButtonText(btn);
                string goName = btn.gameObject?.name ?? "?";
                string parent = btn.transform?.parent?.gameObject?.name ?? "ROOT";
                bool active = btn.gameObject?.activeInHierarchy ?? false;
                sb.AppendLine($"  [{(active ? "ACTIVE" : "hidden")}] parent='{parent}' go='{goName}' text='{txt}'");
                if (++btnIdx >= 120) { sb.AppendLine("  ... (capped at 120)"); break; }
            }
            sb.AppendLine();

            // 3) Active GameObject roots
            sb.AppendLine("--- Active GameObject roots (relevant only) ---");
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var go in roots)
            {
                if (go == null) continue;
                if (IsRelevant(go.name))
                    DumpGameObject(sb, go, 0, 3);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR: {ex.Message}");
        }

        SaveToFile(sb.ToString(), filename);
    }

    public static void DumpRuntimeTypes(string filename = "runtime_types.txt")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== RUNTIME TYPES DUMP {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine("Scanning Assembly-CSharp for classes related to Boss/Mob/NPC/UI...");
        sb.AppendLine();

        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null)
            {
                sb.AppendLine("Assembly-CSharp not loaded!");
                SaveToFile(sb.ToString(), filename);
                return;
            }

            var types = asm.GetTypes();
            sb.AppendLine($"Total types: {types.Length}");

            // Phân loại
            var byKeyword = new Dictionary<string, List<Type>>();
            foreach (var t in types)
            {
                if (t == null) continue;
                var name = t.Name;
                foreach (var kw in RELEVANT_KEYWORDS)
                {
                    if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!byKeyword.ContainsKey(kw)) byKeyword[kw] = new List<Type>();
                        byKeyword[kw].Add(t);
                        break;
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("--- Classes matching relevant keywords ---");
            foreach (var kv in byKeyword.OrderByDescending(x => x.Value.Count))
            {
                sb.AppendLine($"\n  KEYWORD: {kv.Key} ({kv.Value.Count} classes)");
                foreach (var t in kv.Value.Take(40))
                {
                    sb.AppendLine($"    - {t.FullName}");
                }
            }

            // Đặc biệt: list class thừa kế MonoBehaviour
            sb.AppendLine();
            sb.AppendLine("--- MonoBehaviour subclasses (UI candidates) ---");
            int monoCount = 0;
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract) continue;
                if (typeof(MonoBehaviour).IsAssignableFrom(t))
                {
                    if (IsRelevant(t.Name) || t.Name.EndsWith("Panel") || t.Name.EndsWith("Menu"))
                    {
                        sb.AppendLine($"  - {t.FullName} : {t.BaseType?.Name}");
                        monoCount++;
                        if (monoCount > 80) break;
                    }
                }
            }

            // List các field/method quan trọng của GameManager, MainPlayer, Mob, NPC, CapsulePanel
            sb.AppendLine();
            sb.AppendLine("--- Key class members (GameManager, MainPlayer, Mob, NPC, CapsulePanel) ---");
            string[] keyClasses = { "GameManager", "MainPlayer", "Mob", "NPC", "CapsulePanel", "ChangeMap",
                "ZoneMenu", "ZonePanel", "ChangeZone", "InventoryPanel", "DeathPanel", "MiniMap" };
            foreach (var cn in keyClasses)
            {
                var t = asm.GetType(cn);
                if (t == null) continue;
                sb.AppendLine($"\n  === {cn} ===");
                sb.AppendLine("    Static methods:");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Take(15))
                {
                    sb.AppendLine($"      {m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})");
                }
                sb.AppendLine("    Instance methods (public+nonpublic):");
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Take(30))
                {
                    sb.AppendLine($"      {m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})");
                }
                sb.AppendLine("    Fields (public+nonpublic):");
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Take(20))
                {
                    sb.AppendLine($"      {f.FieldType.Name} {f.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR: {ex.Message}");
        }

        SaveToFile(sb.ToString(), filename);
    }

    private static bool IsRelevant(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLower();
        return RELEVANT_KEYWORDS.Any(k => lower.Contains(k));
    }

    private static void DumpInstance(StringBuilder sb, object instance)
    {
        try
        {
            var type = instance.GetType();
            sb.AppendLine($"    Type: {type.FullName}");
            sb.AppendLine("    Fields:");
            foreach (var fi in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try
                {
                    var v = fi.GetValue(instance);
                    var vStr = v == null ? "null" : v.ToString();
                    if (vStr.Length > 80) vStr = vStr.Substring(0, 80) + "...";
                    sb.AppendLine($"      {fi.FieldType.Name} {fi.Name} = {vStr}");
                }
                catch { }
            }
        }
        catch { }
    }

    private static void DumpGameObject(StringBuilder sb, GameObject go, int depth, int maxDepth)
    {
        if (go == null || depth > maxDepth) return;
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}[{go.name}] active={go.activeInHierarchy}");
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;
            sb.AppendLine($"{indent}  - {comp.GetType().Name}");
        }
        foreach (Transform child in go.transform)
        {
            DumpGameObject(sb, child.gameObject, depth + 1, maxDepth);
        }
    }

    private static string GetButtonText(UnityEngine.UI.Button btn)
    {
        try
        {
            foreach (var text in btn.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (!string.IsNullOrEmpty(text.text)) return text.text;
            foreach (var text in btn.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if (!string.IsNullOrEmpty(text.text)) return text.text;
        }
        catch { }
        return "";
    }

    private static void SaveToFile(string content, string filename)
    {
        try
        {
            // Ghi vào <game_root>/BepInEx/dump/ để dễ quản lý
            // Assembly.Location = .../BepInEx/plugins/AutoBossGrabber.dll
            // => lên 2 cấp = <game_root>, rồi vào BepInEx/dump/
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            var dumpDir = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "BepInEx", "dump"));
            Directory.CreateDirectory(dumpDir);
            var path = Path.Combine(dumpDir, filename);
            File.WriteAllText(path, content);
            Plugin.Log.LogInfo($"[UI Dump] Saved to: {path} ({content.Length} chars)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[UI Dump] Save failed: {ex.Message}");
            // Fallback: ghi vào BepInEx root
            try
            {
                var fallback = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", filename);
                File.WriteAllText(fallback, content);
                Plugin.Log.LogInfo($"[UI Dump] Fallback saved to: {fallback}");
            }
            catch { }
        }
    }
}