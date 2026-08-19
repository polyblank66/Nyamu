using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Nyamu.Tools.CodeExecution;
using UnityEditor;
using UnityEditor.Compilation;

namespace Nyamu.EditorTests
{
    // Guards the reference set handed to the ad-hoc code_execute compiler. Unity ships every
    // framework facade twice - once under NetStandard/compat/2.1.0/shims and once under
    // UnityReferenceAssemblies/unity-4.8-api/Facades - and a project references both copies.
    // Letting both reach Roslyn raises CS1703 and fails every code_execute call, and because the
    // duplicate used to be resolved by enumeration order it surfaced only in some projects.
    [TestFixture]
    public class CodeCompilerReferenceTests
    {
        // Synthetic paths - nothing here is opened. Selection keys off the file name and the
        // /NetStandard/ segment only, so the editor's install location is irrelevant; the root is
        // left relative so it cannot be mistaken for a real one, and
        // SelectReferences_DoesNotDependOnEditorInstallLocation pins that down.
        const string EditorData = "EditorData";
        const string ShimEmit = EditorData + "/NetStandard/compat/2.1.0/shims/netstandard/System.Reflection.Emit.dll";
        const string FacadeEmit = EditorData + "/UnityReferenceAssemblies/unity-4.8-api/Facades/System.Reflection.Emit.dll";
        const string ProjectDll = "Library/ScriptAssemblies/Assembly-CSharp.dll";

        // Unity reports Windows separators, so normalization has to be exercised on every host.
        static string Windows(string path) => path.Replace('/', '\\');

        [Test]
        public void SelectReferences_DropsCandidatesTheBuilderAlreadySupplies()
        {
            var selected = CodeCompilerService.SelectReferences(
                new[] { ShimEmit, FacadeEmit, ProjectDll },
                new[] { Windows(ShimEmit) });

            Assert.That(selected, Is.EquivalentTo(new[] { ProjectDll }),
                "A facade the builder references itself must not be passed again under any path.");
        }

        // The regression: with the 4.8 facade seen first, the old dedupe kept it and it collided
        // with the NetStandard copy AssemblyBuilder supplies.
        [Test]
        public void SelectReferences_IgnoresCandidateOrder()
        {
            var defaults = new[] { "Library/ScriptAssemblies/Nyamu.dll" };
            var shimFirst = CodeCompilerService.SelectReferences(new[] { ShimEmit, FacadeEmit, ProjectDll }, defaults);
            var facadeFirst = CodeCompilerService.SelectReferences(new[] { FacadeEmit, ShimEmit, ProjectDll }, defaults);

            Assert.That(facadeFirst, Is.EqualTo(shimFirst), "Selection must not depend on enumeration order.");
        }

        // Locks in which copy wins when the exclusion cannot run - defaultReferences is read
        // defensively, and the fallback has to match the profile the builder actually uses.
        [Test]
        public void SelectReferences_PrefersTheNetStandardCopy()
        {
            var selected = CodeCompilerService.SelectReferences(
                new[] { FacadeEmit, ShimEmit },
                Array.Empty<string>());

            Assert.That(selected, Is.EqualTo(new[] { ShimEmit }));
        }

        [Test]
        public void SelectReferences_NormalizesSeparatorsAndCasing()
        {
            var selected = CodeCompilerService.SelectReferences(
                new[] { FacadeEmit, Windows(ShimEmit).ToUpperInvariant() },
                Array.Empty<string>());

            Assert.That(selected.Length, Is.EqualTo(1), "Paths differing only in separator or case are the same assembly.");
            Assert.That(selected[0], Is.EqualTo(Windows(ShimEmit).ToUpperInvariant()));
        }

        [Test]
        public void SelectReferences_SkipsNullAndEmptyCandidates()
        {
            var selected = CodeCompilerService.SelectReferences(
                new[] { null, string.Empty, ProjectDll },
                Array.Empty<string>());

            Assert.That(selected, Is.EqualTo(new[] { ProjectDll }));
        }

        // The editor may sit on any drive, path, or inside a macOS bundle. Guards against anyone
        // reintroducing an absolute or drive-rooted assumption in the selection.
        [TestCase("S:/Unity/Hub/2022.3.62f2/Editor/Data")]
        [TestCase("C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Data")]
        [TestCase("/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents")]
        [TestCase("/opt/unity/Editor/Data")]
        public void SelectReferences_DoesNotDependOnEditorInstallLocation(string editorRoot)
        {
            var shim = editorRoot + "/NetStandard/compat/2.1.0/shims/netstandard/System.Reflection.Emit.dll";
            var facade = editorRoot + "/UnityReferenceAssemblies/unity-4.8-api/Facades/System.Reflection.Emit.dll";

            Assert.That(CodeCompilerService.SelectReferences(new[] { facade, shim }, Array.Empty<string>()),
                Is.EqualTo(new[] { shim }), "The NetStandard copy must win wherever the editor is installed.");
            Assert.That(CodeCompilerService.SelectReferences(new[] { shim, facade }, new[] { Windows(shim) }),
                Is.Empty, "A facade the builder supplies must be dropped under either path or separator.");
        }

        // End-to-end over the real project: whatever the compiler ends up with, no assembly may
        // appear under two different paths.
        [Test]
        public void RealReferenceSet_NeverPairsADefaultWithADifferentPath()
        {
            var dir = "Temp/Nyamu/ReferenceTests";
            Directory.CreateDirectory(dir);
            var source = dir + "/Probe.cs";
            File.WriteAllText(source, "class Probe {}");

            var builder = new AssemblyBuilder(dir + "/Probe.dll", source)
            {
                flags = AssemblyBuilderFlags.EditorAssembly,
                referencesOptions = ReferencesOptions.UseEngineModules,
                buildTarget = EditorUserBuildSettings.activeBuildTarget,
                buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup
            };
            builder.compilerOptions.ApiCompatibilityLevel = ApiCompatibilityLevel.NET_Unity_4_8;

            var defaults = builder.defaultReferences ?? Array.Empty<string>();
            Assert.That(defaults, Is.Not.Empty, "defaultReferences must be readable before Build(), the exclusion depends on it.");

            var selected = CodeCompilerService.SelectReferences(CodeCompilerService.CollectProjectReferences(), defaults);

            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in defaults)
                byName[Path.GetFileName(reference)] = reference;

            var conflicts = selected
                .Where(r => byName.ContainsKey(Path.GetFileName(r)))
                .Select(r => $"{Path.GetFileName(r)}: {r} vs {byName[Path.GetFileName(r)]}")
                .ToArray();

            Assert.That(conflicts, Is.Empty,
                "These assemblies would reach Roslyn twice and raise CS1703 - "
                + string.Join(" | ", conflicts));
            Assert.That(selected.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Is.EqualTo(selected.Length), "The selected set itself contains a duplicated assembly name.");
        }
    }
}
