using Elsa.Workflows;
using Elsa.Workflows.Helpers;
using WorkflowManager.Endpoints;
using System.Reflection;

namespace WorkflowManager.Endpoints;

public static class DesignerEndpoints
{
    public static IEndpointRouteBuilder MapDesignerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/designer/activities", (IActivityRegistry registry) =>
        {
            var activityTypeMap = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(GetLoadableTypes)
                .Where(t =>
                    typeof(IActivity).IsAssignableFrom(t) &&
                    t.IsClass &&
                    !t.IsAbstract &&
                    !t.IsInterface &&
                    !t.IsGenericType)
                .GroupBy(t => ActivityTypeNameHelper.GenerateTypeName(t))
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            var activities = registry.ListAll()
                .Select(x =>
                {
                    activityTypeMap.TryGetValue(x.TypeName, out var clrType);

                    var inputs = clrType is null
                        ? Array.Empty<DesignerActivityPropertyDto>()
                        : GetPropertiesWithAttributeName(clrType, "InputAttribute");

                    var outputs = clrType is null
                        ? Array.Empty<DesignerActivityPropertyDto>()
                        : GetPropertiesWithAttributeName(clrType, "OutputAttribute");

                    var assemblyName = clrType?.Assembly.GetName().Name ?? "";

                    var source =
                        assemblyName.Equals("WorkflowManager", StringComparison.OrdinalIgnoreCase)
                            ? "Projeto"
                            : assemblyName.StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase)
                                ? "Elsa"
                                : string.IsNullOrWhiteSpace(assemblyName)
                                    ? "Registry"
                                    : "DLL";

                    return new DesignerActivityDto
                    {
                        TypeName = x.TypeName,
                        Version = x.Version,
                        Name = x.Name,
                        DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.Name : x.DisplayName,
                        Category = x.Category,
                        Namespace = x.Namespace,
                        Source = source,
                        AssemblyName = assemblyName,
                        Inputs = inputs,
                        Outputs = outputs
                    };
                })
                .OrderBy(x => x.Source)
                .ThenBy(x => x.Category)
                .ThenBy(x => x.DisplayName)
                .ToArray();

            return Results.Ok(new
            {
                count = activities.Length,
                activities
            });
        });

        return app;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static DesignerActivityPropertyDto[] GetPropertiesWithAttributeName(
        Type type,
        string attributeName)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttributes(inherit: true)
                .Any(a => a.GetType().Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new DesignerActivityPropertyDto
            {
                Name = p.Name,
                PropertyType = p.PropertyType.FullName ?? p.PropertyType.Name
            })
            .OrderBy(p => p.Name)
            .ToArray();
    }
}

public sealed class DesignerActivityDto
{
    public string TypeName { get; set; } = "";
    public int Version { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Category { get; set; }
    public string? Namespace { get; set; }
    public string Source { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public DesignerActivityPropertyDto[] Inputs { get; set; } = Array.Empty<DesignerActivityPropertyDto>();
    public DesignerActivityPropertyDto[] Outputs { get; set; } = Array.Empty<DesignerActivityPropertyDto>();
}

public sealed class DesignerActivityPropertyDto
{
    public string Name { get; set; } = "";
    public string PropertyType { get; set; } = "";
}