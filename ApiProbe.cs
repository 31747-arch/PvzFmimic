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
