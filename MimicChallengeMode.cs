using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace PvzFmimic;

internal static class MimicChallengeMode
{
    private const int MimicPlantId = 5006;
    private const int ChallengeNumber = 200;
    private static bool _installed;
    private static bool _registered;
    private static MelonLogger.Instance _logger;

    public static void Install(MelonLogger.Instance logger)
    {
        if (_installed) return;
        _logger = logger;
        try
        {
            RegisterChallenge();
            InstallAutoSelectHooks();
            _installed = true;
        }
        catch (Exception ex)
        {
            logger.Warning("[Mimic/Challenge] Setup failed: " + ex);
        }
    }

    private static void RegisterChallenge()
    {
        var game = FindAssembly("Assembly-CSharp");
        if (game == null) throw new InvalidOperationException("Assembly-CSharp is not loaded.");
        var challengeType = game.GetType("Il2CppGameLevel.ChallengeLevelData", false);
        var managerType = game.GetType("Il2CppGameLevel.LevelManager", false);
        var levelType = FindType("Il2Cpp.LevelType");
        var challengeEnum = FindType("Il2Cpp.ChallengeLevel");
        if (challengeType == null || managerType == null || levelType == null || challengeEnum == null)
            throw new TypeLoadException("ChallengeLevelData, LevelManager, or a required enum was not found.");

        var challenge = Activator.CreateInstance(challengeType);
        if (challenge == null) throw new InvalidOperationException("Could not construct ChallengeLevelData.");

        SetMember(challenge, "LevelType", Enum.ToObject(levelType, 1));
        SetMember(challenge, "LevelNumber", ChallengeNumber);
        SetMember(challenge, "Level", Enum.ToObject(challengeEnum, ChallengeNumber));
        SetMember(challenge, "Name", "Mimic: Copycat Mayhem");
        SetMember(challenge, "MaxWave", 10);

        var sceneType = FindType("Il2Cpp.SceneType");
        if (sceneType != null) SetMember(challenge, "SceneType", ParseEnumOrFirst(sceneType, "Day"));

        var configType = game.GetTypes().FirstOrDefault(t => t.Name == "BoardConfig");
        if (configType != null)
        {
            try
            {
                var config = Activator.CreateInstance(configType);
                if (config != null) SetMember(challenge, "Config", config);
            }
            catch (Exception ex) { _logger?.Warning("[Mimic/Challenge] BoardConfig setup: " + ex.Message); }
        }

        var zombieType = FindType("Il2Cpp.ZombieType");
        var spawnProperty = challengeType.GetProperty("SpawnZombies",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (zombieType != null && spawnProperty != null)
        {
            try
            {
                var listType = spawnProperty.PropertyType;
                var zombies = Activator.CreateInstance(listType);
                var add = listType.GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
                if (zombies != null && add != null)
                {
                    add.Invoke(zombies, new[] { ParseEnumOrFirst(zombieType, "Normal") });
                    SetMember(challenge, "SpawnZombies", zombies);
                }
            }
            catch (Exception ex) { _logger?.Warning("[Mimic/Challenge] Zombie setup: " + ex.Message); }
        }

        var registry = managerType.GetProperty("registry",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
        if (registry == null) throw new InvalidOperationException("LevelManager.registry is not initialized.");

        var register = registry.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "RegisterPredefinedLevel" && m.GetParameters().Length == 1 &&
                                 m.GetParameters()[0].ParameterType.IsAssignableFrom(challengeType));
        if (register == null) throw new MissingMethodException(registry.GetType().FullName, "RegisterPredefinedLevel");

        var tryGet = registry.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "TryGetLevel" && m.GetParameters().Length == 3);
        if (tryGet != null)
        {
            var p = tryGet.GetParameters();
            var args = new object[] { Enum.ToObject(p[0].ParameterType, 1), ChallengeNumber, null };
            try
            {
                if (Convert.ToBoolean(tryGet.Invoke(registry, args)))
                {
                    _logger?.Warning("[Mimic/Challenge] Challenge slot 200 is occupied; it was not overwritten.");
                    _registered = true;
                    return;
                }
            }
            catch { }
        }

        register.Invoke(registry, new[] { challenge });
        _registered = true;
        _logger?.Msg("[Mimic/Challenge] Registered 'Mimic: Copycat Mayhem' in the Challenge registry (level 200).");
    }

    private static void InstallAutoSelectHooks()
    {
        var game = FindAssembly("Assembly-CSharp");
        var harmony = new global::HarmonyLib.Harmony("31747-arch.pvzfusion.mimic.challenge");
        var postfix = typeof(MimicChallengeMode).GetMethod(nameof(ChallengeInitCardPostfix),
            BindingFlags.Static | BindingFlags.NonPublic);
        var strategyBase = game.GetType("Il2Cpp.BaseLevelStrategy", false);
        var patched = 0;

        if (strategyBase != null)
        {
            foreach (var type in game.GetTypes().Where(t => strategyBase.IsAssignableFrom(t)))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == "InitCard" && m.GetParameters().Length == 1))
            {
                try { harmony.Patch(method, postfix: new HarmonyMethod(postfix)); patched++; }
                catch { }
            }
        }

        var initBoard = game.GetTypes().FirstOrDefault(t => t.Name == "InitBoard");
        var addCard = initBoard?.GetMethod("AddCard", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        if (addCard != null)
        {
            try { harmony.Patch(addCard, postfix: new HarmonyMethod(postfix)); patched++; }
            catch { }
        }
        _logger?.Msg("[Mimic/Challenge] Mimic auto-select hooks installed: " + patched + ".");
    }

    private static void ChallengeInitCardPostfix(object __instance)
    {
        if (__instance == null || !_registered || !IsMimicChallengeActive()) return;
        try
        {
            var preSelect = __instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "PreSelect" && m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType.IsEnum);
            if (preSelect == null) return;
            preSelect.Invoke(__instance, new[] { Enum.ToObject(preSelect.GetParameters()[0].ParameterType, MimicPlantId) });
            _logger?.Msg("[Mimic/Challenge] Mimic auto-selected.");
        }
        catch (Exception ex) { _logger?.Warning("[Mimic/Challenge] Auto-select failed: " + ex.Message); }
    }

    private static bool IsMimicChallengeActive()
    {
        try
        {
            var manager = FindType("Il2CppGameLevel.LevelManager");
            var method = manager?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "TryGetLevelData" && m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].IsOut);
            if (method == null) return false;
            var args = new object[] { null };
            if (!Convert.ToBoolean(method.Invoke(null, args)) || args[0] == null) return false;
            return Convert.ToInt32(FindMember(args[0], "LevelType")) == 1 &&
                   Convert.ToInt32(FindMember(args[0], "LevelNumber")) == ChallengeNumber;
        }
        catch { return false; }
    }

    private static object ParseEnumOrFirst(Type type, string preferred)
    {
        try { return Enum.Parse(type, preferred, true); }
        catch { return Enum.GetValues(type).GetValue(0); }
    }

    private static object FindMember(object instance, string name)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetGetMethod(true) != null) return property.GetValue(instance);
        return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static bool SetMember(object instance, string name, object value)
    {
        if (instance == null) return false;
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true) { property.SetValue(instance, value); return true; }
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) return false;
        field.SetValue(instance, value);
        return true;
    }

    private static Type FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }

    private static Assembly FindAssembly(string name) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
}
