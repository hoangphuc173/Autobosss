using System;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;

namespace AutoBossGrabber;

/// <summary>
/// Dump các class liên quan đến network message từ Assembly-CSharp.
/// Sử dụng: gọi DumpNetworkClasses() trong Plugin.Awake() hoặc qua hotkey.
/// </summary>
public static class NetworkClassDumper
{
    public static void DumpNetworkClasses(ManualLogSource log)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== NETWORK MESSAGE CLASSES DUMP ===");
        sb.AppendLine($"Time: {DateTime.Now}");
        sb.AppendLine();

        try
        {
            // Tìm Assembly-CSharp
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

            if (asm == null)
            {
                sb.AppendLine("ERROR: Assembly-CSharp not found!");
                log.LogError(sb.ToString());
                return;
            }

            sb.AppendLine($"Assembly: {asm.FullName}");
            sb.AppendLine();

            // Tìm các class có keyword liên quan đến message/network
            var keywords = new[] { "Message", "Packet", "Network", "Socket", "Command", "Cmd" };
            var types = asm.GetTypes()
                .Where(t => keywords.Any(k => t.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.Name)
                .ToList();

            sb.AppendLine($"Found {types.Count} network-related classes:");
            sb.AppendLine();

            foreach (var type in types)
            {
                sb.AppendLine($"--- {type.FullName} ---");

                // Dump methods
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.DeclaringType == type)
                    .OrderBy(m => m.Name)
                    .Take(20); // limit 20 methods per class

                foreach (var method in methods)
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    sb.AppendLine($"  {method.ReturnType.Name} {method.Name}({parameters})");
                }

                // Dump fields
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(f => f.DeclaringType == type)
                    .OrderBy(f => f.Name)
                    .Take(20);

                if (fields.Any())
                {
                    sb.AppendLine("  Fields:");
                    foreach (var field in fields)
                    {
                        sb.AppendLine($"    {field.FieldType.Name} {field.Name}");
                    }
                }

                sb.AppendLine();
            }

            // Tìm các class có tên chứa "Boss"
            sb.AppendLine("=== BOSS-RELATED CLASSES ===");
            var bossTypes = asm.GetTypes()
                .Where(t => t.Name.Contains("Boss", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Name)
                .ToList();

            foreach (var type in bossTypes)
            {
                sb.AppendLine($"- {type.FullName}");
            }
            sb.AppendLine();

            // Tìm các class có tên chứa "Notification" hoặc "Alert"
            sb.AppendLine("=== NOTIFICATION/ALERT CLASSES ===");
            var notifTypes = asm.GetTypes()
                .Where(t => t.Name.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
                           t.Name.Contains("Alert", StringComparison.OrdinalIgnoreCase) ||
                           t.Name.Contains("Announce", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Name)
                .ToList();

            foreach (var type in notifTypes)
            {
                sb.AppendLine($"- {type.FullName}");
            }

            log.LogInfo(sb.ToString());

            // Save to file
            try
            {
                var path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "network_classes_dump.txt");
                System.IO.File.WriteAllText(path, sb.ToString());
                log.LogInfo($"[NetworkDump] Saved to: {path}");
            }
            catch (Exception ex)
            {
                log.LogWarning($"[NetworkDump] Failed to save file: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"ERROR: {ex}");
            log.LogError(sb.ToString());
        }
    }
}
