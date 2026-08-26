using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AutoBossGrabber;

public static class UIHelper
{
    public static string GetButtonText(Button btn)
    {
        try
        {
            if (btn == null) return "";
            foreach (var text in btn.GetComponentsInChildren<Text>(true))
                if (!string.IsNullOrEmpty(text.text)) return text.text.Trim();
            foreach (var text in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (!string.IsNullOrEmpty(text.text)) return text.text.Trim();
        }
        catch { }
        return "";
    }

    public static string GetShortcutText(Button btn)
    {
        try
        {
            if (btn == null) return "";
            foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;
                string path = tmp.transform?.parent?.gameObject?.name ?? "";
                if (path.IndexOf("Shortcut", StringComparison.OrdinalIgnoreCase) >= 0)
                    return tmp.text.Trim();
            }
            foreach (var txt in btn.GetComponentsInChildren<Text>(true))
            {
                if (txt == null || string.IsNullOrEmpty(txt.text)) continue;
                string path = txt.transform?.parent?.gameObject?.name ?? "";
                if (path.IndexOf("Shortcut", StringComparison.OrdinalIgnoreCase) >= 0)
                    return txt.text.Trim();
            }
        }
        catch { }
        return "";
    }

    public static string GetTransformPath(Transform tr)
    {
        try
        {
            if (tr == null) return "";
            var parts = new List<string>();
            int guard = 0;
            while (tr != null && guard++ < 12)
            {
                parts.Add(tr.name ?? "");
                tr = tr.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
        catch { }
        return "";
    }
}
