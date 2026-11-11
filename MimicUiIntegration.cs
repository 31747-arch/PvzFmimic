using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace PvzFmimic;

// Uses the game's existing Imitater entry (PlantType 245) as a safe,
// visible placeholder until a dedicated Mimic plant type and assets exist.
internal static class MimicUiIntegration
{
    private const int ExistingMimicLikePlantId = 245; // Imitater
    private static bool _seedAdded;
    private static bool _almanacGroupAdded;

    public static void Install(MelonLogger.Instance logger)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (assembly == null)
            {
                logger.Warning("[Mimic/UI] Assembly-CSharp not loaded; UI patches skipped.");
                return;
            }

            var harmony = new global::HarmonyLib.Harmony("31747-arch.pvzfusion.mimic.ui");
            Patch(assembly, harmony, "InitBoard", "AddCard", nameof(AddSeedPostfix), logger);
            Patch(assembly, harmony, "PlantCardPackageBuilder", "Start", nameof(AlmanacStartPostfix), logger);
        }
        catch (Exception ex)
        {
            logger.Warning("[Mimic/UI] Could not install UI integration: " + ex);
        }
    }

    private static void Patch(Assembly assembly, global::HarmonyLib.Harmony harmony, string typeName,
        string methodName, string postfixName, MelonLogger.Instance logger)
    {
        var type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
        var target = type?.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        var postfix = typeof(MimicUiIntegration).GetMethod(postfixName,
            BindingFlags.Static | BindingFlags.NonPublic);
        if (target == null || postfix == null)
        {
            logger.Warning("[Mimic/UI] Could not find " + typeName + "." + methodName + ".");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        logger.Msg("[Mimic/UI] Patched " + typeName + "." + methodName + ".");
    }

    private static void AddSeedPostfix(object __instance)
    {
        if (_seedAdded || __instance == null) return;
        _seedAdded = true;
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
                MelonLogger.Warning("[Mimic/Seed] CreateCard(PlantType,bool,bool) not found.");
                return;
            }

            var plantType = Enum.ToObject(createCard.GetParameters()[0].ParameterType,
                ExistingMimicLikePlantId);
            var card = createCard.Invoke(__instance, new object[] { plantType, false, false });
            if (card == null)
            {
                MelonLogger.Warning("[Mimic/Seed] Game returned no Imitater card.");
                return;
            }

            MelonLogger.Msg("[Mimic/Seed] Added the built-in Imitater seed as a temporary Mimic test entry (id 245).");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Mimic/Seed] Safe seed insertion failed: " + ex);
        }
    }

    private static void AlmanacStartPostfix(object __instance)
    {
        if (_almanacGroupAdded || __instance == null) return;
        _almanacGroupAdded = true;
        try
        {
            var builderType = __instance.GetType();
            var registerGroup = builderType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "RegisterGroup" &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(string));
            if (registerGroup == null)
            {
                MelonLogger.Warning("[Mimic/Almanac] RegisterGroup(string, List<PlantType>) not found.");
                return;
            }

            var plantType = registerGroup.GetParameters()[1].ParameterType.GetGenericArguments().FirstOrDefault();
            if (plantType == null || !plantType.IsEnum)
            {
                MelonLogger.Warning("[Mimic/Almanac] Could not resolve the game's PlantType enum.");
                return;
            }

            var list = Activator.CreateInstance(registerGroup.GetParameters()[1].ParameterType);
            var add = list.GetType().GetMethod("Add");
            add.Invoke(list, new[] { Enum.ToObject(plantType, ExistingMimicLikePlantId) });

            registerGroup.Invoke(__instance, new[] { (object)"Mimics", list });
            MelonLogger.Msg("[Mimic/Almanac] Registered the 'Mimics' group with Mimic/Imitater as its first entry.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Mimic/Almanac] Could not add Mimics group safely: " + ex);
        }
    }
}
