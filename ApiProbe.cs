using System;
using System.Linq;
using System.Reflection;

namespace PvzFmimic;

internal static class ApiProbe
{
    private static readonly string[] InterestingTypes =
    {
        "PlantType", "PlantData", "PlantDevelopData", "DynamicPlantEvolutionData",
        "PlantEvolutionData", "CustomizedPlant", "CardUI", "SeedBank", "Plant",
        "GardenUI", "PlantTypeToStringNode"
    };

    public static void Dump(Action<string> log)
    {
        try
        {
            var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));

            if (gameAssembly == null)
            {
                log("[Mimic/API] Assembly-CSharp is not loaded yet.");
                return;
            }

            log("[Mimic/API] Inspecting game API in " + gameAssembly.FullName);

            foreach (var typeName in InterestingTypes)
            {
                var type = gameAssembly.GetTypes()
                    .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
                if (type == null)
                {
                    log("[Mimic/API] Type not found: " + typeName);
                    continue;
                }

                log("[Mimic/API] TYPE " + type.FullName);
                if (type.IsEnum)
                {
                    var names = Enum.GetNames(type);
                    log("[Mimic/API] enum values (" + names.Length + "): " + string.Join(", ", names.Take(240)));
                    continue;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Static | BindingFlags.Instance |
                                           BindingFlags.DeclaredOnly;
                foreach (var field in type.GetFields(flags).Take(60))
                    log("[Mimic/API] FIELD " + field.FieldType.Name + " " + field.Name);
                foreach (var property in type.GetProperties(flags).Take(60))
                    log("[Mimic/API] PROPERTY " + property.PropertyType.Name + " " + property.Name);
                foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName).Take(100))
                {
                    var parameters = string.Join(", ", method.GetParameters()
                        .Select(p => p.ParameterType.Name + " " + p.Name));
                    log("[Mimic/API] METHOD " + method.ReturnType.Name + " " + method.Name + "(" + parameters + ")");
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            log("[Mimic/API] Reflection type-load warning: " +
                string.Join(" | ", ex.LoaderExceptions.Where(e => e != null).Select(e => e.Message).Take(8)));
        }
        catch (Exception ex)
        {
            log("[Mimic/API] Probe failed: " + ex);
        }
    }
}
