using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nyamu.Tools.CodeExecution
{
    // Reflective, depth-limited formatter for arbitrary code_execute return values. JsonUtility
    // cannot serialize `object`, so every result is rendered to a plain string up front.
    internal static class ResultFormatter
    {
        const int MaxCollectionItems = 50;

        public static (string text, string typeName) Describe(object value, int maxChars)
        {
            var cap = maxChars > 0 ? maxChars : 4000;
            var text = DescribeValue(value, 0);
            if (text.Length > cap)
                text = text.Substring(0, cap) + " …(truncated)";

            var typeName = value?.GetType().FullName ?? "";
            return (JsonSafe.Sanitize(text), typeName);
        }

        static string DescribeValue(object value, int depth)
        {
            if (value == null)
                return "null";

            if (value is string s)
                return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            var type = value.GetType();

            if (type.IsPrimitive || value is decimal || value is Enum || value is DateTime || value is TimeSpan || value is Guid)
                return SafeToString(value);

            if (value is Type t)
                return t.FullName;

            if (value is UnityEngine.Object unityObj)
                return DescribeUnityObject(unityObj);

            if (depth >= 1)
                return SafeToString(value);

            if (value is IDictionary dict)
                return DescribeDictionary(dict);

            if (value is IEnumerable enumerable)
                return DescribeEnumerable(enumerable);

            return DescribeObject(value, type);
        }

        static string DescribeUnityObject(UnityEngine.Object obj)
        {
            try
            {
                if (obj == null)
                    return "null (destroyed UnityEngine.Object)";

                var path = "";
                try { path = AssetDatabase.GetAssetPath(obj); } catch { /* not an asset */ }

                var pathPart = string.IsNullOrEmpty(path) ? "" : $", path=\"{path}\"";
                return $"{obj.GetType().Name} \"{obj.name}\" (instanceID={obj.GetInstanceID()}{pathPart})";
            }
            catch (Exception ex)
            {
                return $"<error describing UnityEngine.Object: {ex.Message}>";
            }
        }

        static string DescribeDictionary(IDictionary dict)
        {
            var sb = new StringBuilder("{");
            var count = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (count >= MaxCollectionItems)
                {
                    sb.Append($", …(+{dict.Count - count} more)");
                    break;
                }
                if (count > 0)
                    sb.Append(", ");
                sb.Append(SafeDescribe(entry.Key)).Append(": ").Append(SafeDescribe(entry.Value));
                count++;
            }
            sb.Append('}');
            return sb.ToString();
        }

        static string DescribeEnumerable(IEnumerable enumerable)
        {
            var items = new List<string>();
            var total = 0;
            foreach (var item in enumerable)
            {
                total++;
                if (items.Count < MaxCollectionItems)
                    items.Add(SafeDescribe(item));
            }
            var suffix = total > items.Count ? $" (+{total - items.Count} more)" : "";
            return $"Count={total} [{string.Join(", ", items)}]{suffix}";
        }

        static string DescribeObject(object value, Type type)
        {
            var parts = new List<string>();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { parts.Add($"{field.Name}={SafeDescribe(field.GetValue(value))}"); }
                catch { /* Unity properties throw constantly (destroyed objects, wrong thread); skip rather than fail the whole format */ }
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                try { parts.Add($"{prop.Name}={SafeDescribe(prop.GetValue(value))}"); }
                catch { /* ignored, see above */ }
            }

            return $"{type.FullName} {{ {string.Join(", ", parts)} }}";
        }

        // Depth is always passed as 1 here: DescribeValue only recurses one level deep, which
        // also sidesteps needing cycle detection.
        static string SafeDescribe(object value)
        {
            try { return DescribeValue(value, 1); }
            catch (Exception ex) { return $"<error: {ex.Message}>"; }
        }

        static string SafeToString(object value)
        {
            try { return Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString(); }
            catch { return value.ToString(); }
        }
    }
}
