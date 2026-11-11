using MelonLoader;

[assembly: MelonInfo(typeof(PvzFmimic.MimicMod), "PvZ Fusion Mimic", "1.0.1", "31747-arch")]

namespace PvzFmimic;

public sealed class MimicMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("PvZ Fusion Mimic loaded.");
        LoggerInstance.Msg("Running a safe game-API inspection to prepare the Mimic plant.");
        ApiProbe.Dump(message => LoggerInstance.Msg(message));
        MimicSeedInjection.Install(LoggerInstance);
    }
}
