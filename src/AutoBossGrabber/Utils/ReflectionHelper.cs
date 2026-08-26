using System;
using System.Reflection;

namespace AutoBossGrabber;

public static class ReflectionHelper
{
    public static object InvokeNoArg(object obj, string methodName)
    {
        try
        {
            if (obj == null) return null;
            var m = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null && m.GetParameters().Length == 0)
                return m.Invoke(obj, null);
        }
        catch { }
        return null;
    }

    public static object GetMemberValue(object obj, string name)
    {
        try
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = t.GetProperty(name, flags);
            if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                return p.GetValue(obj, null);
            var f = t.GetField(name, flags);
            if (f != null)
                return f.GetValue(obj);
        }
        catch { }
        return null;
    }

    public static bool TryGetIntMember(object obj, out int value, params string[] names)
    {
        value = 0;
        if (obj == null) return false;
        try
        {
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var name in names)
            {
                var m = t.GetMethod(name, flags);
                if (m != null && m.GetParameters().Length == 0)
                {
                    var v = m.Invoke(obj, null);
                    if (v != null)
                    {
                        value = Convert.ToInt32(v);
                        return true;
                    }
                }

                var p = t.GetProperty(name, flags);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    var v = p.GetValue(obj, null);
                    if (v != null)
                    {
                        value = Convert.ToInt32(v);
                        return true;
                    }
                }

                var f = t.GetField(name, flags);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    if (v != null)
                    {
                        value = Convert.ToInt32(v);
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }
}