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

            var types = assembly.GetTypes();
            log("[Mimic/API] Targeted registration scan in " + assembly.FullName);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.Instance |
                                       BindingFlags.DeclaredOnly;

            var candidates = types.Where(t =>
                    t.Name.IndexOf("Plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Seed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.Name.IndexOf("Card", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(t => t.Name.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Bank", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Develop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Chooser", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            t.Name.IndexOf("Plant", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t.FullName)
                .Take(100)
                .ToArray();

            log("[Mimic/API] Candidate types: " + candidates.Length);
            foreach (var type in candidates)
            {
                var methods = type.GetMethods(flags).Where(m => !m.IsSpecialName).ToArray();
                var interesting = methods.Where(m =>
                    new[] { "Create", "Register", "Add", "Load", "Init", "Get", "Set", "PlantData", "Card" }
                        .Any(k => m.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(24)
                    .ToArray();

                if (interesting.Length == 0 && !type.Name.Contains("PlantData") &&
                    !type.Name.Contains("Seed") && !type.Name.Contains("Card"))
                    continue;

                log("[Mimic/API] TYPE " + type.FullName);
                foreach (var method in interesting)
                {
                    var parameters = string.Join(", ", method.GetParameters()
                        .Select(p => p.ParameterType.FullName + " " + p.Name));
                    log("[Mimic/API] METHOD " + method.ReturnType.FullName + " " + method.Name + "(" + parameters + ")" +
                        (method.IsStatic ? " [static]" : ""));
                }

                foreach (var field in type.GetFields(flags).Where(x =>
                             x.Name.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             x.Name.IndexOf("card", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             x.Name.IndexOf("seed", StringComparison.OrdinalIgnoreCase) >= 0).Take(12))
                    log("[Mimic/API] FIELD " + field.FieldType.FullName + " " + field.Name);
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
