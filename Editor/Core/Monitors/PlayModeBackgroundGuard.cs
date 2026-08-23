using UnityEditor;
using UnityEngine;

namespace Nyamu.Core.Monitors
{
    // Keeps the Editor's main loop ticking while Play Mode runs without window focus.
    //
    // With "Run In Background" disabled in Player Settings, a playing Editor that loses window
    // focus stops serving Nyamu entirely: not just the main-thread tools, but every endpoint,
    // editor_status included. Nothing recovers until the Editor window is clicked again - and
    // since editor_exit_play_mode is unreachable too, an agent that entered Play Mode cannot get
    // itself out.
    //
    // The obvious half of the mechanism is the action queue: everything touching a Unity API is
    // queued onto UnityThreadExecutor and drained from a single place - EditorApplication.update,
    // via EditorMonitor.OnEditorUpdate - and Unity suspends that loop on focus loss. But that
    // alone would leave TcpHttpServer answering, because it serves each connection on the thread
    // pool and the status tools only read cached state. Observed behaviour says otherwise, so
    // something suspends more than the Editor loop. The exact mechanism is not established; see
    // Documentation/TODO.md. It does mean a server-side "honest timeout" cannot be the answer -
    // a process that cannot reply cannot explain itself either.
    //
    // Application.runInBackground is a runtime-only override: Unity re-applies
    // PlayerSettings.runInBackground on every Play Mode entry, so forcing it here never writes to
    // ProjectSettings.asset, never shows up in version control, and never reaches a build.
    // Public to match the other monitors: EditorMonitor's constructor takes one, and that
    // constructor is public.
    public sealed class PlayModeBackgroundGuard
    {
        // Logged once per Play Mode session so game code that fights the override every frame
        // cannot flood the console.
        bool _loggedThisSession;

        // The whole policy, kept free of Unity API calls so it can be tested off the Editor loop.
        internal static bool ShouldForce(bool settingEnabled, bool isPlaying, bool runInBackground)
            => settingEnabled && isPlaying && !runInBackground;

        public void Initialize() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        public void Cleanup() => EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // Applied here as well as from the tick below: EnteredPlayMode is guaranteed to be
            // raised, whereas the first update tick of an already-unfocused Play Mode session is
            // exactly what the suspension might swallow.
            if (change == PlayModeStateChange.EnteredPlayMode)
                Apply(true);
            else if (change == PlayModeStateChange.ExitingPlayMode)
                _loggedThisSession = false;
        }

        // Called once per EditorApplication.update tick from EditorMonitor, using the play mode
        // sample that tick already took. Re-asserting every tick rather than only on
        // EnteredPlayMode is deliberate: user code is free to set Application.runInBackground to
        // false from Awake/Start/Update, and the tick that notices still runs, because the Editor
        // is necessarily focused for as long as it has not been alt-tabbed away from yet.
        public void Tick(bool isPlaying) => Apply(isPlaying);

        void Apply(bool isPlaying)
        {
            if (!isPlaying)
                return;

            bool settingEnabled;
            try
            {
                settingEnabled = NyamuSettings.Instance.keepPlayModeRunningUnfocused;
            }
            catch
            {
                // Settings can be unavailable mid-reload; skipping a tick is harmless because the
                // next one re-evaluates.
                return;
            }

            if (!ShouldForce(settingEnabled, true, Application.runInBackground))
                return;

            Application.runInBackground = true;

            if (_loggedThisSession)
                return;

            _loggedThisSession = true;
            NyamuLogger.LogInfo(
                "[Nyamu][PlayModeBackgroundGuard] Forced Application.runInBackground while in Play Mode " +
                "so MCP requests keep being served when the Editor is unfocused. " +
                "Disable 'Keep Play Mode Running Unfocused' in Project Settings > Nyamu MCP Server to opt out.");
        }
    }
}
