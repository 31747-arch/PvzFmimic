using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace PvzFmimic;

// Uses the game's built-in Imitater (PlantType 245) as a temporary Mimic
// entry until the mod has its own registered plant type and assets.
internal static class MimicUiIntegration
{
    private const int ExistingMimicLikePlantId = 245;
    private static bool _seedAdded;
    private static bool _almanacTabAdded;
    private static object _almanacMenu;

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
            Patch(assembly, harmony, "AlmanacPlantMenu", "Awake", nameof(AlmanacAwakePostfix), logger);
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

    // The previous implementation registered a group on PlantCardPackageBuilder.
    // That is the seed-package UI, not the Almanac. The real Almanac class is
    // AlmanacPlantMenu, whose ShowPlants(List<PlantType>) method fills its grid.
    private static void AlmanacAwakePostfix(object __instance)
    {
        if (_almanacTabAdded || __instance == null) return;

        try
        {
            _almanacMenu = __instance;
            var menuType = __instance.GetType();
            var sourceButtonProperty = menuType.GetProperty("button_lookMix",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var sourceButton = sourceButtonProperty?.GetValue(__instance);
            if (sourceButton == null)
            {
                MelonLogger.Warning("[Mimic/Almanac] button_lookMix was null; cannot create the Mimics tab.");
                return;
            }

            var clone = CloneButtonBeside(sourceButton);
            if (clone == null)
            {
                MelonLogger.Warning("[Mimic/Almanac] Could not clone the existing Almanac category button.");
                return;
            }

            SetButtonLabel(clone, "Mimics");
            if (!WireButtonClick(clone))
            {
                MelonLogger.Warning("[Mimic/Almanac] Could not connect the Mimics button click event.");
                return;
            }

            _almanacTabAdded = true;
            MelonLogger.Msg("[Mimic/Almanac] Added the Mimics category button to AlmanacPlantMenu.");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Mimic/Almanac] Could not add the Mimics tab: " + ex);
        }
    }

    private static object CloneButtonBeside(object sourceButton)
    {
        var gameObjectType = FindType("UnityEngine.GameObject");
        var unityObjectType = FindType("UnityEngine.Object");
        if (gameObjectType == null || unityObjectType == null) return null;

        var transformProperty = gameObjectType.GetProperty("transform");
        var sourceTransform = transformProperty?.GetValue(sourceButton);
        if (sourceTransform == null) return null;

        var transformType = sourceTransform.GetType();
        var parent = transformType.GetProperty("parent")?.GetValue(sourceTransform);
        var instantiate = unityObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "Instantiate" || m.IsGenericMethod || m.GetParameters().Length != 2)
                    return false;
                var p = m.GetParameters();
                return p[0].ParameterType.IsAssignableFrom(gameObjectType) &&
                       p[1].ParameterType.IsAssignableFrom(transformType);
            });
        if (instantiate == null) return null;

        var clone = instantiate.Invoke(null, new[] { sourceButton, parent });
        if (clone == null) return null;

        gameObjectType.GetProperty("name")?.SetValue(clone, "Mimic_AlmanacCategoryButton");
        var cloneTransform = transformProperty.GetValue(clone);
        var setParent = cloneTransform.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "SetParent" && m.GetParameters().Length == 2);
        if (setParent != null && parent != null)
            setParent.Invoke(cloneTransform, new[] { parent, (object)false });

        // Put the duplicate one button-width to the right of the Mix button.
        var rectType = FindType("UnityEngine.RectTransform");
        var rect = rectType == null ? null : GetComponent(sourceButton, rectType);
        var cloneRect = rectType == null ? null : GetComponent(clone, rectType);
        if (rect != null && cloneRect != null)
        {
            var sizeDelta = rectType.GetProperty("sizeDelta")?.GetValue(rect);
            var width = ReadVectorX(sizeDelta);
            var positionProperty = rectType.GetProperty("anchoredPosition");
            var position = positionProperty?.GetValue(cloneRect);
            if (position != null && positionProperty != null)
            {
                var moved = SetVectorX(position, ReadVectorX(position) + (width > 0 ? width + 8f : 120f));
                positionProperty.SetValue(cloneRect, moved);
            }
        }

        return clone;
    }

    private static bool WireButtonClick(object gameObject)
    {
        var buttonType = FindType("UnityEngine.UI.Button");
        if (buttonType == null) return false;
        var button = GetComponent(gameObject, buttonType);
        var clickEvent = buttonType.GetProperty("onClick")?.GetValue(button);
        if (clickEvent == null) return false;

        var addListener = clickEvent.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "AddListener" && m.GetParameters().Length == 1);
        if (addListener == null) return false;

        var actionType = addListener.GetParameters()[0].ParameterType;
        var handler = typeof(MimicUiIntegration).GetMethod(nameof(OnMimicsClicked),
            BindingFlags.Static | BindingFlags.NonPublic);
        var callback = Delegate.CreateDelegate(actionType, handler);
        clickEvent.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "RemoveAllListeners" && m.GetParameters().Length == 0)
            ?.Invoke(clickEvent, null);
        addListener.Invoke(clickEvent, new object[] { callback });
        return true;
    }

    private static void OnMimicsClicked()
    {
        try
        {
            if (_almanacMenu == null) return;
            var showPlants = _almanacMenu.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "ShowPlants" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsGenericType);
            if (showPlants == null)
            {
                MelonLogger.Warning("[Mimic/Almanac] ShowPlants(List<PlantType>) not found.");
                return;
            }

            var listType = showPlants.GetParameters()[0].ParameterType;
            var plantType = listType.GetGenericArguments().FirstOrDefault();
            if (plantType == null || !plantType.IsEnum) return;

            var list = Activator.CreateInstance(listType);
            listType.GetMethod("Add")?.Invoke(list,
                new[] { Enum.ToObject(plantType, ExistingMimicLikePlantId) });
            showPlants.Invoke(_almanacMenu, new[] { list });
            MelonLogger.Msg("[Mimic/Almanac] Displayed the Mimic test entry (Imitater, id 245).");
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Mimic/Almanac] Could not display Mimics: " + ex);
        }
    }

    private static void SetButtonLabel(object gameObject, string label)
    {
        var tmpTextType = FindType("TMPro.TMP_Text");
        if (tmpTextType == null) return;
        var components = GetComponentsInChildren(gameObject, tmpTextType);
        if (components is IEnumerable enumerable)
        {
            foreach (var component in enumerable)
            {
                var property = component?.GetType().GetProperty("text");
                if (property != null && property.CanWrite)
                {
                    property.SetValue(component, label);
                    return;
                }
            }
        }

        var single = GetComponentInChildren(gameObject, tmpTextType);
        single?.GetType().GetProperty("text")?.SetValue(single, label);
    }

    private static object GetComponent(object gameObject, Type componentType)
    {
        var method = gameObject.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "GetComponent" && !m.IsGenericMethod &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
        return method?.Invoke(gameObject, new object[] { componentType });
    }

    private static object GetComponentInChildren(object gameObject, Type componentType)
    {
        var method = gameObject.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "GetComponentInChildren" && !m.IsGenericMethod &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
        return method?.Invoke(gameObject, new object[] { componentType });
    }

    private static object GetComponentsInChildren(object gameObject, Type componentType)
    {
        var method = gameObject.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "GetComponentsInChildren" && !m.IsGenericMethod &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
        return method?.Invoke(gameObject, new object[] { componentType });
    }

    private static float ReadVectorX(object vector)
    {
        if (vector == null) return 0f;
        var type = vector.GetType();
        var field = type.GetField("x");
        if (field != null) return Convert.ToSingle(field.GetValue(vector));
        var property = type.GetProperty("x");
        return property == null ? 0f : Convert.ToSingle(property.GetValue(vector));
    }

    private static object SetVectorX(object vector, float x)
    {
        var type = vector.GetType();
        var copy = Activator.CreateInstance(type);
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            field.SetValue(copy, field.GetValue(vector));
        var xField = type.GetField("x");
        if (xField != null) xField.SetValue(copy, x);
        else type.GetProperty("x")?.SetValue(copy, x);
        return copy;
    }

    private static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }
}
