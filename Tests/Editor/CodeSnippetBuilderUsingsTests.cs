using NUnit.Framework;
using Nyamu.Tools.CodeExecution;

namespace Nyamu.EditorTests
{
    // Guards the using prologue CodeSnippetBuilder puts in front of an ad-hoc code_execute snippet.
    // Class mode used to be the one mode that got no defaults, so a snippet saying 'Application' or
    // 'EditorApplication' failed with CS0103 while the tool documented those namespaces as always
    // available.
    [TestFixture]
    public class CodeSnippetBuilderUsingsTests
    {
        static string BuildSource(string code, string mode, string[] usings = null)
        {
            CodeSnippetBuilder.Build(
                new CodeExecuteRequest { code = code, mode = mode, usings = usings },
                out var generatedSource, out _, out _, out _);
            return generatedSource;
        }

        const string ClassSnippet = "public static class T { public static string Execute() => \"ok\"; }";

        [Test]
        public void ClassMode_EmitsTheUnityDefaultUsings()
        {
            var source = BuildSource(ClassSnippet, "class");

            Assert.That(source, Does.Contain("using UnityEngine;"));
            Assert.That(source, Does.Contain("using UnityEditor;"));
            Assert.That(source, Does.Contain("using System;"));
        }

        // The defaults have to land at file scope, ahead of the snippet's own type declaration -
        // a using directive after a type is a compile error, not a nicety.
        [Test]
        public void ClassMode_EmitsDefaultsBeforeTheSnippet()
        {
            var source = BuildSource(ClassSnippet, "class");

            Assert.That(source.IndexOf("using UnityEngine;", System.StringComparison.Ordinal),
                Is.LessThan(source.IndexOf(ClassSnippet, System.StringComparison.Ordinal)),
                "Directives must precede the snippet's type declaration.");
        }

        [Test]
        public void ExpressionMode_StillEmitsTheDefaults()
        {
            var source = BuildSource("1 + 1", "expression");

            Assert.That(source, Does.Contain("using UnityEngine;"));
            Assert.That(source, Does.Contain("using UnityEditor;"));
        }

        // Caller-supplied namespaces are additive, not a replacement.
        [Test]
        public void ClassMode_KeepsCallerSuppliedUsingsAlongsideTheDefaults()
        {
            var source = BuildSource(ClassSnippet, "class", new[] { "System.IO" });

            Assert.That(source, Does.Contain("using System.IO;"));
            Assert.That(source, Does.Contain("using UnityEngine;"));
        }

        // Every mode is suppressed, because every mode can produce a duplicate: class mode is
        // auto-detected exactly when the snippet opens with its own usings, and any mode can be
        // handed a 'usings' entry that repeats a default.
        [TestCase("class")]
        [TestCase("expression")]
        [TestCase("statements")]
        public void EveryMode_SuppressesTheDuplicateUsingWarning(string mode)
        {
            var code = mode == "class" ? ClassSnippet : "1 + 1";
            var source = BuildSource(code, mode, new[] { "UnityEngine" });

            Assert.That(source, Does.Contain("#pragma warning disable CS0105"),
                "A duplicate using is routine here and the warning gives the caller nothing to act on.");
        }

        // The pragma only works if it opens the file - it takes effect from where it appears.
        [Test]
        public void SuppressionComesFirstInTheGeneratedSource()
        {
            var source = BuildSource(ClassSnippet, "class");

            Assert.That(source.TrimStart(), Does.StartWith("#pragma warning disable CS0105"));
        }
    }
}
