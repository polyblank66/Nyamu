using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Nyamu.Tools.CodeExecution
{
    // Wraps a user-supplied snippet in a compilable source file. Uses #line directives so
    // Roslyn reports compiler diagnostics (and stack traces via the emitted PDB) against the
    // user's own line numbers, not the generated wrapper's.
    internal static class CodeSnippetBuilder
    {
        public const string VirtualFileName = "NyamuUserCode.cs";

        static readonly string[] DefaultUsings =
        {
            "System", "System.Collections", "System.Collections.Generic", "System.Linq",
            "System.Reflection", "System.Text", "System.Threading.Tasks",
            "UnityEngine", "UnityEditor", "Nyamu.CodeExecution"
        };

        static readonly Regex TypeDeclarationRegex = new(
            @"(?m)^\s*(\[[^\]]*\]\s*)*(public|internal|static|abstract|sealed|partial|\s)*\b(class|struct|interface|enum|record)\s+\w",
            RegexOptions.Compiled);

        public static void Build(CodeExecuteRequest request, out string generatedSource, out string resolvedMode,
            out int prologueLineCount, out string virtualFileName)
        {
            virtualFileName = VirtualFileName;
            var requestedMode = string.IsNullOrEmpty(request.mode) ? "auto" : request.mode;
            resolvedMode = requestedMode == "auto" ? DetectMode(request.code) : requestedMode;
            generatedSource = Wrap(request, resolvedMode, out prologueLineCount);
        }

        // Called when an auto-detected expression/statements guess fails to compile - retries
        // once with the other wrapper shape before reporting a compile error to the agent.
        public static string BuildFallback(CodeExecuteRequest request, string failedMode, out string fallbackMode,
            out int prologueLineCount)
        {
            fallbackMode = failedMode == "expression" ? "statements" : "expression";
            return Wrap(request, fallbackMode, out prologueLineCount);
        }

        internal static string DetectMode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "statements";

            var trimmedStart = code.TrimStart();
            if (TypeDeclarationRegex.IsMatch(code) ||
                trimmedStart.StartsWith("using ", StringComparison.Ordinal) ||
                trimmedStart.StartsWith("namespace ", StringComparison.Ordinal))
            {
                return "class";
            }

            var trimmed = code.Trim();
            if (trimmed.IndexOf(';') < 0 && trimmed.IndexOf('{') < 0 && trimmed.IndexOf('\n') < 0)
                return "expression";

            return "statements";
        }

        static string Wrap(CodeExecuteRequest request, string mode, out int prologueLineCount)
        {
            var prefix = new StringBuilder();

            if (mode != "class")
            {
                foreach (var u in DefaultUsings)
                    prefix.Append("using ").Append(u).Append("; ");
                foreach (var u in NormalizeUsings(request.usings))
                    prefix.Append("using ").Append(u).Append("; ");

                prefix.Append("namespace NyamuGenerated { public static class Entry { public static object Execute() {");
                if (mode == "expression")
                    prefix.Append(" return (");
                prefix.Append('\n');
                prefix.Append("#line 1 \"").Append(VirtualFileName).Append("\"\n");
            }
            else
            {
                foreach (var u in NormalizeUsings(request.usings))
                    prefix.Append("using ").Append(u).Append(";\n");
                prefix.Append("#line 1 \"").Append(VirtualFileName).Append("\"\n");
            }

            prologueLineCount = CountLines(prefix.ToString());

            var sb = new StringBuilder();
            sb.Append(prefix);
            sb.Append(request.code);

            if (mode != "class")
            {
                sb.Append("\n#line hidden\n");
                sb.Append(mode == "expression" ? "); } } }\n" : " return null; } } }\n");
            }
            else
            {
                sb.Append('\n');
            }

            return sb.ToString();
        }

        static int CountLines(string s)
        {
            var count = 0;
            foreach (var ch in s)
                if (ch == '\n')
                    count++;
            return count;
        }

        static IEnumerable<string> NormalizeUsings(string[] usings)
        {
            if (usings == null)
                yield break;

            foreach (var raw in usings)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var u = raw.Trim();
                if (u.StartsWith("using ", StringComparison.Ordinal))
                    u = u.Substring(6).Trim();
                u = u.TrimEnd(';', ' ');
                if (u.Length > 0)
                    yield return u;
            }
        }
    }
}
