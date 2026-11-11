using System;
using MelonLoader;

[assembly: MelonInfo(typeof(PvzFmimic.MimicMod), "PvZ Fusion Mimic", "1.0.2", "31747-arch")]

namespace PvzFmimic;

public sealed class MimicMod : MelonMod
{
    private bool _seedHookAttempted;

    public override void OnInitializeMelon()
    {
        // Keep startup deliberately minimal. Reflection-heavy game inspection and
        // Harmony setup are deferred until Unity has loaded a game scene.
        LoggerInstance.Msg("PvZ Fusion Mimic loaded (safe startup mode).");
        LoggerInstance.Msg("Mimic seed setup will wait until the first scene is loaded.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (_seedHookAttempted)
            return;

        _seedHookAttempted = true;
        LoggerInstance.Msg("[Mimic/Init] First scene loaded: " + sceneName +
            " (build index " + buildIndex + "). Starting seed-hook setup.");

        try
        {
            MimicSeedInjection.Install(LoggerInstance);
            LoggerInstance.Msg("[Mimic/Init] Seed-hook setup call completed.");
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning("[Mimic/Init] Seed-hook setup failed: " + ex);
        }

        // Almanac integration is intentionally not initialized yet. The current
        // priority is getting the Mimic seed registered without destabilizing startup.
    }
}
