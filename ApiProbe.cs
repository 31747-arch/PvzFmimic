using System;
using System.Linq;
using System.Reflection;

namespace PvzFmimic;

internal static class ApiProbe
{
    public static void Dump(Action<string> log)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                log("[Mimic/API] Assembly-CSharp is not loaded yet.");
                return;
            }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            log("[Mimic/API] Expanded selection/registration scan in " + assembly.FullName);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.Instance |
                                       BindingFlags.DeclaredOnly;

            bool Has(string value, params string[] keys) =>
                keys.Any(k => value.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

            var selected = types.Where(t =>
                    Has(t.Name, "InitBoard", "PlantType", "PlantMixTree", "PlantDevelop", "PlantEvolution",
                        "DynamicPlant", "CustomizedPlant", "SeedBank", "SeedChooser", "SeedSelect",
                        "PlantSelect", "PlantChoose", "PlantChooser", "Selection", "SelectPlant",
                        "CardPackage", "PlantBank", "CardSlot", "AlmanacPlantMenu", "AlmanacCardUI") ||
                    (Has(t.Name, "Seed", "Card", "Plant") &&
                     Has(t.Name, "Manager", "Data", "Bank", "Chooser", "Choose", "Select", "Menu", "Panel", "UI", "Tree")))
                .OrderBy(t => Has(t.Name, "InitBoard", "PlantType", "SeedBank", "PlantSelect", "PlantChoose", "PlantChooser", "CardSlot") ? 0 : 1)
                .ThenBy(t => t.FullName)
                .Take(220)
                .ToArray();


            // Focused follow-up: constructors and live manager state are needed to
            // safely register a custom plant and recipes. Keep this separate from
            // the broad scan so the next log is easy to extract.
            var criticalNames = new[]
            {
                "PlantMixTreeManager", "PlantMixTreeNode", "PlantMixTreeManagerExample",
                "CustomPlantManager", "CustomPlant", "PlantDevelopData", "PlantType",
                "InitBoard", "CardUI", "CardSlotManager"
            };

            foreach (var name in criticalNames)
            {
                var type = types.FirstOrDefault(t =>
                    string.Equals(t.Name, name, StringComparison.Ordinal));
                if (type == null) continue;

                log("[Mimic/DETAIL] TYPE " + type.FullName + (type.IsEnum ? " [ENUM]" : ""));
                if (type.IsEnum)
                {
                    foreach (var enumName in Enum.GetNames(type))
                    {
                        try { log("[Mimic/DETAIL] ENUM " + enumName + " = " + Convert.ToInt64(Enum.Parse(type, enumName))); }
                        catch { log("[Mimic/DETAIL] ENUM " + enumName); }
                    }
                }

                foreach (var ctor in type.GetConstructors(flags))
                {
                    var parameters = string.Join(", ", ctor.GetParameters()
                        .Select(p => SafeTypeName(p.ParameterType) + " " + p.Name));
                    log("[Mimic/DETAIL] CTOR " + type.Name + "(" + parameters + ")");
                }

                foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName))
                {
                    var parameters = string.Join(", ", method.GetParameters()
                        .Select(p => SafeTypeName(p.ParameterType) + " " + p.Name));
                    log("[Mimic/DETAIL] METHOD " + SafeTypeName(method.ReturnType) + " " + method.Name +
                        "(" + parameters + ")" + (method.IsStatic ? " [static]" : ""));
                }

                foreach (var property in type.GetProperties(flags))
                {
                    var getter = property.GetGetMethod(true);
                    log("[Mimic/DETAIL] PROPERTY " + SafeTypeName(property.PropertyType) + " " + property.Name +
                        (getter != null && getter.IsStatic ? " [static]" : ""));
                    if (getter == null || !getter.IsStatic || property.GetIndexParameters().Length != 0)
                        continue;

                    try
                    {
                        var value = property.GetValue(null);
                        if (value == null)
                            log("[Mimic/DETAIL] VALUE " + property.Name + " = null");
                        else
                        {
                            var countProperty = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
                            if (countProperty != null && countProperty.PropertyType == typeof(int))
                                log("[Mimic/DETAIL] VALUE " + property.Name + " count=" + countProperty.GetValue(value));
                            else
                                log("[Mimic/DETAIL] VALUE " + property.Name + " type=" + SafeTypeName(value.GetType()));
                        }
                    }
                    catch (Exception ex)
                    {
                        log("[Mimic/DETAIL] VALUE " + property.Name + " unavailable: " + ex.GetType().Name);
                    }
                }
            }

            log("[Mimic/API] Relevant types: " + selected.Length);
            foreach (var type in selected)
            {
                log("[Mimic/API] TYPE " + type.FullName + (type.IsEnum ? " [ENUM]" : ""));
                if (type.IsEnum)
                {
                    foreach (var name in Enum.GetNames(type).Take(180))
                    {
                        try { log("[Mimic/API] ENUM " + name + " = " + Convert.ToInt64(Enum.Parse(type, name))); }
                        catch { log("[Mimic/API] ENUM " + name); }
                    }
                }

                foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName).Take(70))
                {
                    var parameters = string.Join(", ", method.GetParameters()
                        .Select(p => SafeTypeName(p.ParameterType) + " " + p.Name));
                    log("[Mimic/API] METHOD " + SafeTypeName(method.ReturnType) + " " + method.Name +
                        "(" + parameters + ")" + (method.IsStatic ? " [static]" : ""));
                }

                foreach (var field in type.GetFields(flags).Take(60))
                    log("[Mimic/API] FIELD " + SafeTypeName(field.FieldType) + " " + field.Name);

                foreach (var property in type.GetProperties(flags).Take(60))
                    log("[Mimic/API] PROPERTY " + SafeTypeName(property.PropertyType) + " " + property.Name);
            }
        }
        catch (Exception ex)
        {
            log("[Mimic/API] Probe failed: " + ex);
        }
    }

    private static string SafeTypeName(Type type)
    {
        try { return type.FullName ?? type.Name; }
        catch { return type.Name; }
    }
}
