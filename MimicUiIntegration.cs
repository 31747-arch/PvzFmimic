using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace PvzFmimic;

// Almanac integration for the Mimic category. Seed injection is deliberately
// disabled until a real custom Mimic plant type is registered.
internal static class MimicUiIntegration
{
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

        // button_lookMix is a UI component in this game, not a GameObject.
        // Resolve its owning GameObject before reading transform or cloning it.
        object sourceGameObject = sourceButton;
        if (!gameObjectType.IsInstanceOfType(sourceGameObject))
        {
            // IL2CPP-generated subclasses can expose inherited Unity properties
            // through reflection with a declaring type that does not match the
            // managed wrapper. Resolve gameObject from UnityEngine.Component
            // itself instead of asking the generated subclass for the property.
            var componentType = FindType("UnityEngine.Component");
            var ownerProperty = componentType?.GetProperty("gameObject",
                BindingFlags.Instance | BindingFlags.Public);
            if (componentType == null || !componentType.IsInstanceOfType(sourceButton) ||
                ownerProperty == null)
                return null;

            sourceGameObject = ownerProperty.GetValue(sourceButton);
        }

        if (sourceGameObject == null || !gameObjectType.IsInstanceOfType(sourceGameObject))
            return null;

        var transformProperty = gameObjectType.GetProperty("transform");
        var sourceTransform = transformProperty?.GetValue(sourceGameObject);
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

        var clone = instantiate.Invoke(null, new[] { sourceGameObject, parent });
        if (clone == null) return null;

        gameObjectType.GetProperty("name")?.SetValue(clone, "Mimic_AlmanacCategoryButton");
        var cloneTransform = transformProperty.GetValue(clone);
        var setParent = cloneTransform.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "SetParent" && m.GetParameters().Length == 2);
        if (setParent != null && parent != null)
            setParent.Invoke(cloneTransform, new[] { parent, (object)false });

        // Put the duplicate one button-width to the right of the Mix button.
        var rectType = FindType("UnityEngine.RectTransform");
        var rect = rectType == null ? null : GetComponent(sourceGameObject, rectType);
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
            // Do not put the built-in Imitater into this list as a fake Mimic.
            // The custom Mimic type must be registered before entries are displayed.
            MelonLogger.Warning("[Mimic/Almanac] Mimic list is not populated yet: custom Mimic PlantType is not registered.");
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
