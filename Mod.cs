using MelonLoader;

namespace PvzFmimic;

public sealed class MimicMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("PvZ Fusion Mimic loaded.");
        LoggerInstance.Msg("Running a safe game-API inspection to prepare the Mimic plant.");
        ApiProbe.Dump(message => LoggerInstance.Msg(message));
    }
}
