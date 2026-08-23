using NUnit.Framework;
using Nyamu.Core.Monitors;

namespace Nyamu.EditorTests
{
    // Pins the policy behind the Play Mode focus stall: with "Run In Background" off, Unity
    // suspends EditorApplication.update once a playing Editor loses focus, which strands
    // UnityThreadExecutor's queue and hangs every main-thread MCP tool - editor_exit_play_mode
    // included, so the Editor can only be recovered by hand. PlayModeBackgroundGuard forces the
    // runtime override on to keep the loop alive.
    [TestFixture]
    public class PlayModeBackgroundGuardTests
    {
        // The case the whole guard exists for.
        [Test]
        public void ShouldForce_WhenPlayingWithBackgroundExecutionOff()
        {
            Assert.That(PlayModeBackgroundGuard.ShouldForce(settingEnabled: true, isPlaying: true, runInBackground: false),
                Is.True, "A playing Editor that would stop ticking unfocused must be rescued.");
        }

        // Opting out has to mean opting out: someone testing OnApplicationFocus/OnApplicationPause
        // needs Unity's real focus-loss behaviour, stalled MCP tools and all.
        [Test]
        public void ShouldNotForce_WhenSettingDisabled()
        {
            Assert.That(PlayModeBackgroundGuard.ShouldForce(settingEnabled: false, isPlaying: true, runInBackground: false),
                Is.False);
        }

        // Edit Mode never suspends on focus loss - the update loop keeps ticking at a few Hz - so
        // there is nothing to fix, and writing the runtime override there would be pure noise.
        [Test]
        public void ShouldNotForce_WhenNotPlaying()
        {
            Assert.That(PlayModeBackgroundGuard.ShouldForce(settingEnabled: true, isPlaying: false, runInBackground: false),
                Is.False);
        }

        // Projects that already tick "Run In Background" get no write at all: the guard runs once
        // per update tick, so a redundant assignment would be a per-frame native call forever.
        [Test]
        public void ShouldNotForce_WhenAlreadyRunningInBackground()
        {
            Assert.That(PlayModeBackgroundGuard.ShouldForce(settingEnabled: true, isPlaying: true, runInBackground: true),
                Is.False);
        }

        // Guards against the override being re-applied on a project that opted out and already
        // has the Player Setting enabled - neither reason to act is present.
        [Test]
        public void ShouldNotForce_WhenDisabledAndAlreadyRunningInBackground()
        {
            Assert.That(PlayModeBackgroundGuard.ShouldForce(settingEnabled: false, isPlaying: true, runInBackground: true),
                Is.False);
        }
    }
}
