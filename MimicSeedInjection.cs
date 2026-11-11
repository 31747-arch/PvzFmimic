using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace PvzFmimic;

internal static class MimicSeedInjection
{
    // 5006 is intentionally an undefined PlantType value. Fusion's custom-plant
    // ecosystem uses undefined enum values, provided all resource/data entries exist.
    private const int MimicPlantId = 5006;
    private const int BasePlantId = 0; // Peashooter is only the temporary behaviour/prefab base.

    private static bool _installed;
    private static bool _registered;
    private static bool _cardCreated;
    private static bool _plantPlacementPatchInstalled;
    private static MelonLogger.Instance _logger;

    public static void Install(MelonLogger.Instance logger)
    {
        if (_installed) return;
        _logger = logger;

        try
        {
            var game = FindAssembly("Assembly-CSharp");
            if (game == null)
            {
                logger.Warning("[Mimic/Seed] Assembly-CSharp is not loaded; seed registration will retry when installed again.");
                return;
            }

            var initBoard = game.GetTypes().FirstOrDefault(t => t.Name == "InitBoard");
            var addCard = initBoard?.GetMethod("AddCard",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (addCard == null)
            {
                logger.Warning("[Mimic/Seed] InitBoard.AddCard was not found.");
                return;
            }

            var harmony = new global::HarmonyLib.Harmony("31747-arch.pvzfusion.mimic.seed");
            var postfix = typeof(MimicSeedInjection).GetMethod(nameof(AddCardPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(addCard, postfix: new HarmonyMethod(postfix));

            InstallPlantPlacementPatch(game, harmony);
            _installed = true;
            logger.Msg("[Mimic/Seed] Seed registration hooks installed. Waiting for the seed-selection screen.");
        }
        catch (Exception ex)
        {
            logger.Warning("[Mimic/Seed] Could not install seed registration: " + ex);
        }
    }

    private static void AddCardPostfix(object __instance)
    {
        if (__instance == null || _cardCreated) return;

        try
        {
            if (!_registered && !RegisterMimicResources())
                return;

            var createCard = __instance.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "CreateCard" &&
                    m.GetParameters().Length >= 1 &&
                    m.GetParameters()[0].ParameterType.IsEnum &&
                    m.GetParameters().Skip(1).All(p => p.ParameterType == typeof(bool)));

            if (createCard == null)
            {
                _logger?.Warning("[Mimic/Seed] InitBoard.CreateCard(PlantType, ...) was not found.");
                return;
            }

            var parameters = createCard.GetParameters();
            var args = new object[parameters.Length];
            args[0] = Enum.ToObject(parameters[0].ParameterType, MimicPlantId);
            for (var i = 1; i < args.Length; i++)
                args[i] = false;

            var card = createCard.Invoke(__instance, args);
            if (card == null)
            {
                _logger?.Warning("[Mimic/Seed] CreateCard returned null for the Mimic.");
                return;
            }

            _cardCreated = true;
            _logger?.Msg("[Mimic/Seed] Mimic card created successfully (custom PlantType " + MimicPlantId + ").");
        }
        catch (Exception ex)
        {
            _logger?.Warning("[Mimic/Seed] Could not create the Mimic card: " + ex);
        }
    }

    private static bool RegisterMimicResources()
    {
        try
        {
            var game = FindAssembly("Assembly-CSharp");
            if (game == null) return false;

            var gameApp = game.GetTypes().FirstOrDefault(t => t.Name == "GameAPP");
            var resourcesManager = GetStaticMember(gameApp, "resourcesManager");
            if (resourcesManager == null)
            {
                _logger?.Msg("[Mimic/Seed] Waiting for GameAPP.resourcesManager.");
                return false;
            }

            var plantPrefabs = GetMember(resourcesManager, "plantPrefabs");
            var plantPreviews = GetMember(resourcesManager, "plantPreviews");
            var allPlants = GetMember(resourcesManager, "allPlants");
            var plantDataManager = game.GetTypes().FirstOrDefault(t => t.Name == "PlantDataManager");
            var defaultPlantData = GetStaticMember(plantDataManager, "PlantData_Default");
            var unlocked = GetStaticMember(plantDataManager, "unlocked");

            if (plantPrefabs == null || plantPreviews == null || allPlants == null ||
                defaultPlantData == null || unlocked == null)
            {
                _logger?.Warning("[Mimic/Seed] Required game resource collections are not ready yet.");
                return false;
            }

            var plantType = plantPrefabs.GetType().GetGenericArguments().FirstOrDefault();
            if (plantType == null || !plantType.IsEnum) return false;
            var mimicType = Enum.ToObject(plantType, MimicPlantId);
            var baseType = Enum.ToObject(plantType, BasePlantId);

            var sourcePrefab = GetDictionaryValue(plantPrefabs, baseType);
            var sourcePreview = GetDictionaryValue(plantPreviews, baseType);
            var sourceData = GetDictionaryValue(defaultPlantData, baseType);
            if (sourcePrefab == null || sourcePreview == null || sourceData == null)
            {
                _logger?.Warning("[Mimic/Seed] Peashooter prefab, preview, or plant data was not available.");
                return false;
            }

            // Make separate prefab/preview objects so the Peashooter resources remain untouched.
            var mimicPrefab = Instantiate(sourcePrefab);
            var mimicPreview = Instantiate(sourcePreview);
            if (mimicPrefab == null || mimicPreview == null)
            {
                DestroyUnityObject(mimicPrefab);
                DestroyUnityObject(mimicPreview);
                _logger?.Warning("[Mimic/Seed] Could not clone the temporary base resources.");
                return false;
            }

            // Fusion collections may contain either GameObjects or components.
            // Instantiate(component) creates a live scene object too, so always
            // deactivate its owning GameObject rather than calling SetActive on
            // the component wrapper (which silently did nothing before).
            var mimicPrefabObject = GetGameObject(mimicPrefab);
            var mimicPreviewObject = GetGameObject(mimicPreview);
            var prefabDeactivated = mimicPrefabObject != null && SetActive(mimicPrefabObject, false);
            var previewDeactivated = mimicPreviewObject != null && SetActive(mimicPreviewObject, false);
            if (!prefabDeactivated || !previewDeactivated)
            {
                _logger?.Warning("[Mimic/Seed] Clone inspection failed. " +
                    "Prefab source=" + DescribeObject(sourcePrefab) +
                    "; prefab clone=" + DescribeObject(mimicPrefab) +
                    "; prefab GameObject=" + DescribeObject(mimicPrefabObject) +
                    "; prefab SetActive=" + prefabDeactivated +
                    "; preview source=" + DescribeObject(sourcePreview) +
                    "; preview clone=" + DescribeObject(mimicPreview) +
                    "; preview GameObject=" + DescribeObject(mimicPreviewObject) +
                    "; preview SetActive=" + previewDeactivated + ".");
                DestroyUnityObject(mimicPrefab);
                DestroyUnityObject(mimicPreview);
                _logger?.Warning("[Mimic/Seed] Clones were discarded safely; no scene object will be left at the origin.");
                return false;
            }

            SetObjectName(mimicPrefabObject, "Mimic_PlantPrefab");
            SetObjectName(mimicPreviewObject, "Mimic_PlantPreview");
            SetTag(mimicPrefabObject, "Plant");
            SetTag(mimicPreviewObject, "Preview");

            var plantComponentType = game.GetTypes().FirstOrDefault(t => t.Name == "Plant");
            var plantComponent = plantComponentType == null ? null :
                (plantComponentType.IsInstanceOfType(mimicPrefab) ? mimicPrefab : GetComponent(mimicPrefabObject, plantComponentType));
            if (plantComponent == null || !SetMember(plantComponent, "thePlantType", mimicType))
            {
                _logger?.Warning("[Mimic/Seed] Could not assign the custom PlantType to the cloned plant prefab.");
                return false;
            }

            var sprite = CreateMimicPlaceholderSprite();
            if (sprite != null)
            {
                var spriteRendererType = FindType("UnityEngine.SpriteRenderer");
                var spriteRenderer = spriteRendererType == null ? null : GetComponent(mimicPreviewObject, spriteRendererType);
                if (spriteRenderer != null)
                    SetMember(spriteRenderer, "sprite", sprite);
            }

            var dataType = sourceData.GetType();
            var mimicData = ClonePlantData(dataType, sourceData, mimicType);
            if (mimicData == null)
            {
                _logger?.Warning("[Mimic/Seed] Could not clone Peashooter's PlantData.");
                return false;
            }

            SetDictionaryValue(plantPrefabs, mimicType, mimicPrefab);
            SetDictionaryValue(plantPreviews, mimicType, mimicPreview);
            SetDictionaryValue(defaultPlantData, mimicType, mimicData);
            SetDictionaryValue(unlocked, mimicType, true);
            AddListValue(allPlants, mimicType);

            _registered = true;
            _logger?.Msg("[Mimic/Seed] Registered custom PlantType " + MimicPlantId +
                " in plantPrefabs, plantPreviews, allPlants, PlantData_Default, and unlocked.");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warning("[Mimic/Seed] Resource registration failed safely: " + ex);
            return false;
        }
    }

    private static void InstallPlantPlacementPatch(Assembly game, global::HarmonyLib.Harmony harmony)
    {
        if (_plantPlacementPatchInstalled) return;
        var createPlant = game.GetTypes().FirstOrDefault(t => t.Name == "CreatePlant");
        if (createPlant == null) return;

        var postfix = typeof(MimicSeedInjection).GetMethod(nameof(PlantCreatedPostfix),
            BindingFlags.Static | BindingFlags.NonPublic);
        var patched = 0;
        foreach (var method in createPlant.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(m => m.Name == "SetPlant" && m.ReturnType.Name == "Plant"))
        {
            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            patched++;
        }

        _plantPlacementPatchInstalled = patched > 0;
        if (_plantPlacementPatchInstalled)
            _logger?.Msg("[Mimic/Seed] Installed placement identity patch on " + patched + " CreatePlant.SetPlant overload(s).");
    }

    private static void PlantCreatedPostfix(object __result)
    {
        if (__result == null) return;
        try
        {
            var game = FindAssembly("Assembly-CSharp");
            var mouseType = game?.GetTypes().FirstOrDefault(t => t.Name == "Mouse");
            var mouse = GetStaticMember(mouseType, "Instance");
            var selected = GetMember(mouse, "thePlantTypeOnMouse");
            if (selected == null || Convert.ToInt32(selected) != MimicPlantId) return;

            var plantTypeProperty = __result.GetType().GetProperty("thePlantType",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (plantTypeProperty != null && plantTypeProperty.CanWrite)
                plantTypeProperty.SetValue(__result, Enum.ToObject(plantTypeProperty.PropertyType, MimicPlantId));
            else
                SetMember(__result, "thePlantType", Enum.ToObject(FindType("Il2Cpp.PlantType"), MimicPlantId));

            // Let the game's own placement lifecycle activate and position the plant.
            // Manually activating here can leave a detached object at the scene origin.
        }
        catch (Exception ex)
        {
            _logger?.Warning("[Mimic/Seed] Could not assign Mimic identity after placement: " + ex.Message);
        }
    }

    private static object ClonePlantData(Type dataType, object source, object mimicType)
    {
        object clone = null;
        var copyCtor = dataType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(c => c.GetParameters().Length == 1 &&
                                 c.GetParameters()[0].ParameterType.IsAssignableFrom(source.GetType()));
        if (copyCtor != null)
            clone = copyCtor.Invoke(new[] { source });
        else
        {
            clone = Activator.CreateInstance(dataType);
            if (clone == null) return null;
            foreach (var field in dataType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                try { field.SetValue(clone, field.GetValue(source)); } catch { }
            }
            foreach (var property in dataType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;
                try { property.SetValue(clone, property.GetValue(source)); } catch { }
            }
        }

        return SetMember(clone, "thePlantType", mimicType) ? clone : null;
    }

    private static object CreateMimicPlaceholderSprite()
    {
        try
        {
            var textureType = FindType("UnityEngine.Texture2D");
            var formatType = FindType("UnityEngine.TextureFormat");
            var colorType = FindType("UnityEngine.Color");
            var rectType = FindType("UnityEngine.Rect");
            var vectorType = FindType("UnityEngine.Vector2");
            var spriteType = FindType("UnityEngine.Sprite");
            if (textureType == null || formatType == null || colorType == null ||
                rectType == null || vectorType == null || spriteType == null) return null;

            var format = Enum.Parse(formatType, "RGBA32");
            var ctor = textureType.GetConstructors().FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length == 4 && p[0].ParameterType == typeof(int) &&
                       p[1].ParameterType == typeof(int) && p[2].ParameterType == formatType &&
                       p[3].ParameterType == typeof(bool);
            });
            if (ctor == null) return null;
            var texture = ctor.Invoke(new[] { (object)64, 64, format, false });

            var colorCtor = colorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            var setPixel = textureType.GetMethod("SetPixel", new[] { typeof(int), typeof(int), colorType });
            if (colorCtor == null || setPixel == null) return null;

            object MakeColor(float r, float g, float b, float a) =>
                colorCtor.Invoke(new object[] { r, g, b, a });

            var transparent = MakeColor(0f, 0f, 0f, 0f);
            var outline = MakeColor(0.10f, 0.12f, 0.16f, 1f);
            var top = MakeColor(0.42f, 0.78f, 0.98f, 1f);
            var left = MakeColor(0.20f, 0.52f, 0.82f, 1f);
            var right = MakeColor(0.12f, 0.36f, 0.66f, 1f);

            var silhouette = new[] { 32, 59, 57, 45, 57, 18, 32, 5, 7, 18, 7, 45 };
            var topFace = new[] { 32, 55, 52, 44, 32, 33, 12, 44 };
            var leftFace = new[] { 10, 42, 30, 31, 30, 10, 10, 20 };
            var rightFace = new[] { 34, 31, 54, 42, 54, 20, 34, 10 };

            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                object pixel = transparent;
                if (InsidePolygon(x, y, silhouette)) pixel = outline;
                if (InsidePolygon(x, y, leftFace)) pixel = left;
                if (InsidePolygon(x, y, rightFace)) pixel = right;
                if (InsidePolygon(x, y, topFace)) pixel = top;
                setPixel.Invoke(texture, new[] { (object)x, y, pixel });
            }

            var apply = textureType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Apply");
            if (apply != null)
            {
                var applyArgs = apply.GetParameters().Select(p =>
                    p.ParameterType == typeof(bool) ? (object)(p.Name == "updateMipmaps") : null).ToArray();
                apply.Invoke(texture, applyArgs);
            }
            var rect = Activator.CreateInstance(rectType, new object[] { 0f, 0f, 64f, 64f });
            var pivot = Activator.CreateInstance(vectorType, new object[] { 0.5f, 0.5f });
            var create = spriteType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 4);
            return create?.Invoke(null, new[] { texture, rect, pivot, (object)64f });
        }
        catch (Exception ex)
        {
            _logger?.Warning("[Mimic/Seed] Cube placeholder sprite generation failed: " + ex);
            return null;
        }
    }

    private static bool InsidePolygon(int x, int y, int[] points)
    {
        var inside = false;
        for (int i = 0, j = points.Length - 2; i < points.Length; j = i, i += 2)
        {
            var xi = points[i];
            var yi = points[i + 1];
            var xj = points[j];
            var yj = points[j + 1];
            var intersects = ((yi > y) != (yj > y)) &&
                (x < (double)(xj - xi) * (y - yi) / (yj - yi) + xi);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    private static object Instantiate(object original)
    {
        if (original == null) return null;
        var unityObject = FindType("UnityEngine.Object");
        var method = unityObject?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Instantiate" && !m.IsGenericMethod &&
                                 m.GetParameters().Length == 1 &&
                                 m.GetParameters()[0].ParameterType.IsAssignableFrom(original.GetType()));
        return method?.Invoke(null, new[] { original });
    }

    private static object GetComponent(object gameObject, Type componentType)
    {
        if (gameObject == null || componentType == null) return null;
        var method = gameObject.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "GetComponent" && !m.IsGenericMethod &&
                                 m.GetParameters().Length == 1 &&
                                 m.GetParameters()[0].ParameterType == typeof(Type));
        return method?.Invoke(gameObject, new object[] { componentType });
    }

    private static object GetGameObject(object obj)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        for (var current = type; current != null; current = current.BaseType)
        {
            var setActive = current.GetMethod("SetActive",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new[] { typeof(bool) }, null);
            if (setActive != null) return obj;
        }

        // Il2CppInterop wrappers can expose inherited Unity properties differently
        // from ordinary managed Unity objects, so inspect the full type hierarchy.
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty("gameObject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property?.GetGetMethod(true) != null)
            {
                try { return property.GetValue(obj); } catch { }
            }

            var field = current.GetField("gameObject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                try { return field.GetValue(obj); } catch { }
            }
        }
        return null;
    }

    private static string DescribeObject(object obj)
    {
        if (obj == null) return "<null>";
        var type = obj.GetType();
        var hasSetActive = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(m => m.Name == "SetActive" && m.GetParameters().Length == 1 &&
                      m.GetParameters()[0].ParameterType == typeof(bool));
        var hasGameObjectMember = false;
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null ||
                current.GetField("gameObject", BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
            {
                hasGameObjectMember = true;
                break;
            }
        }
        return type.FullName + " [SetActive(bool)=" + hasSetActive +
               ", gameObject member=" + hasGameObjectMember + "]";
    }

    private static void SetObjectName(object obj, string name) => SetMember(obj, "name", name);
    private static void SetTag(object obj, string tag) => SetMember(obj, "tag", tag);

    private static bool SetActive(object gameObject, bool active)
    {
        if (gameObject == null) return false;
        var method = gameObject.GetType().GetMethod("SetActive",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null);
        if (method == null) return false;
        method.Invoke(gameObject, new object[] { active });
        return true;
    }

    private static void DestroyUnityObject(object obj)
    {
        var target = GetGameObject(obj) ?? obj;
        if (target == null) return;
        try
        {
            var unityObject = FindType("UnityEngine.Object");
            var destroy = unityObject?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Destroy" && m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType.IsAssignableFrom(target.GetType()));
            destroy?.Invoke(null, new[] { target });
        }
        catch { }
    }

    private static object GetDictionaryValue(object dictionary, object key)
    {
        if (dictionary == null) return null;
        var method = dictionary.GetType().GetMethods().FirstOrDefault(m =>
            m.Name == "get_Item" && m.GetParameters().Length == 1);
        try { return method?.Invoke(dictionary, new[] { key }); }
        catch { return null; }
    }

    private static void SetDictionaryValue(object dictionary, object key, object value)
    {
        var type = dictionary.GetType();
        var contains = type.GetMethods().FirstOrDefault(m => m.Name == "ContainsKey" && m.GetParameters().Length == 1);
        var exists = contains != null && Convert.ToBoolean(contains.Invoke(dictionary, new[] { key }));
        if (exists)
        {
            var setter = type.GetMethods().FirstOrDefault(m => m.Name == "set_Item" && m.GetParameters().Length == 2);
            if (setter != null) setter.Invoke(dictionary, new[] { key, value });
            else type.GetProperty("Item")?.SetValue(dictionary, value, new[] { key });
        }
        else
        {
            var add = type.GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 2);
            if (add == null) throw new MissingMethodException(type.FullName, "Add");
            add.Invoke(dictionary, new[] { key, value });
        }
    }

    private static void AddListValue(object list, object value)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        var type = list.GetType();
        var contains = type.GetMethods().FirstOrDefault(m => m.Name == "Contains" && m.GetParameters().Length == 1);
        if (contains != null && Convert.ToBoolean(contains.Invoke(list, new[] { value }))) return;
        var add = type.GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
        if (add == null) throw new MissingMethodException(type.FullName, "Add");
        add.Invoke(list, new[] { value });
    }

    private static object GetStaticMember(Type type, string name)
    {
        if (type == null) return null;
        var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetGetMethod(true) != null) return property.GetValue(null);
        return type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static object GetMember(object instance, string name)
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
        if (property?.CanWrite == true)
        {
            property.SetValue(instance, value);
            return true;
        }

        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) return false;
        field.SetValue(instance, value);
        return true;
    }

    private static Assembly FindAssembly(string name) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);

    private static Type FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }
}
