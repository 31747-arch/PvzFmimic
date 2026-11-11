using MelonLoader;

namespace PvzFmimic;

public sealed class MimicMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("PvZ Fusion Mimic loaded.");
        LoggerInstance.Msg("Mimic feature implementation is being initialized.");
    }
}
