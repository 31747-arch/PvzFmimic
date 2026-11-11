using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace PvzFmimic;

internal static class MimicSeedInjection
{
    private const int MimicPlantId = 5006;
    private static bool _attempted;

    public static void Install(MelonLogger.Instance logger)
    {
        try
        {
            var game = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var initBoard = game?.GetTypes().FirstOrDefault(t => t.Name == "InitBoard");
            var addCard = initBoard?.GetMethod("AddCard",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (addCard == null)
            {
                logger.Msg("[Mimic/Seed] InitBoard.AddCard was not found; seed injection not installed.");
                return;
            }

            var harmony = new global::HarmonyLib.Harmony("31747-arch.pvzfusion.mimic.seed");
            var postfix = typeof(MimicSeedInjection).GetMethod(nameof(AddCardPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(addCard, postfix: new HarmonyMethod(postfix));
            logger.Msg("[Mimic/Seed] Seed injection patch installed (custom plant id " + MimicPlantId + ").");
        }
        catch (Exception ex)
        {
            logger.Warning("[Mimic/Seed] Could not install seed injection: " + ex);
        }
    }

    private static void AddCardPostfix(object __instance)
    {
        if (_attempted || __instance == null) return;
        _attempted = true;

        try
        {
            var createCard = __instance.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "CreateCard" &&
                    m.GetParameters().Length == 3 &&
                    m.GetParameters()[0].ParameterType.IsEnum &&
                    m.GetParameters()[1].ParameterType == typeof(bool) &&
                    m.GetParameters()[2].ParameterType == typeof(bool));

            if (createCard == null)
            {
                MelonLogger.Warning("[Mimic/Seed] InitBoard.CreateCard(PlantType,bool,bool) not found.");
                return;
            }

            var plantType = Enum.ToObject(createCard.GetParameters()[0].ParameterType, MimicPlantId);
            var card = createCard.Invoke(__instance, new object[] { plantType, false, false });
            if (card == null)
            {
                MelonLogger.Warning("[Mimic/Seed] CreateCard returned null for Mimic.");
                return;
            }

            var manager = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp")?
                .GetTypes().FirstOrDefault(t => t.Name == "CustomPlantManager");
            var setCustomCard = manager?.GetMethod("SetCustomCard",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (setCustomCard != null)
            {
                var result = setCustomCard.Invoke(null, new[] { card });
                MelonLogger.Msg("[Mimic/Seed] Mimic seed card created; SetCustomCard=" + result + ".");
            }
            else
            {
                MelonLogger.Warning("[Mimic/Seed] Mimic card created, but CustomPlantManager.SetCustomCard was not found.");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Mimic/Seed] Seed injection failed safely: " + ex);
        }
    }
}
