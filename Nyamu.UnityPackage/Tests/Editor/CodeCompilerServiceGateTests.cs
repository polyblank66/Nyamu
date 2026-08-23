using NUnit.Framework;
using Nyamu.Tools.CodeExecution;

namespace Nyamu.EditorTests
{
    // Guards the busy gate in front of code_execute's AssemblyBuilder. The gate used to read
    // EditorApplication.isCompiling, which is also true while a script compilation is merely
    // *pending* - a state Unity holds indefinitely in Play Mode or with assembly reloads locked.
    // One pending edit could therefore lock code_execute out for the rest of the session while
    // editor_status reported isCompiling: false. It now reads the same compilation state
    // editor_status does, so the two cannot disagree.
    [TestFixture]
    public class CodeCompilerServiceGateTests
    {
        static CodeExecutionRecord NewRecord(string id) =>
            new(id, new CodeExecuteRequest { code = "1 + 1", mode = "expression" },
                "public static class T {}", "expression", 0, "NyamuUserCode.cs");

        [Test]
        public void RejectsAsBusyWhileTheProjectIsCompiling()
        {
            var called = false;
            var started = CodeCompilerService.StartBuild(NewRecord("gate_busy"), true,
                (r, m) => called = true, out var error, out var outcome);

            Assert.That(started, Is.False);
            Assert.That(outcome, Is.EqualTo(CodeCompilerService.BusyOutcome));
            Assert.That(error, Is.EqualTo("Unity is compiling."));
            Assert.That(called, Is.False, "The gate must reject before any build is set up.");
        }

        // Proves the gate itself lets the call through when the project is not compiling, without
        // paying for a real AssemblyBuilder run: the id contains a character no path can hold, so
        // the very first step past the gate - creating the Temp output directory - throws. Landing
        // on build_rejected rather than editor_busy is the whole point.
        [Test]
        public void PassesTheGateWhenTheProjectIsNotCompiling()
        {
            var started = CodeCompilerService.StartBuild(NewRecord("gate|invalid"), false,
                (r, m) => { }, out var error, out var outcome);

            Assert.That(started, Is.False);
            Assert.That(outcome, Is.EqualTo(CodeCompilerService.RejectedOutcome),
                "A pending-but-not-running compilation must not be reported as busy.");
            Assert.That(error, Does.StartWith("Failed to start compilation:"));
        }
    }
}
