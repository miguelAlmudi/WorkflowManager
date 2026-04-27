using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using WorkflowManager.Client.Pages;
using WorkflowManager.Components;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using WorkflowManager.Activities;
using Elsa.Workflows.Helpers;
using WorkflowManager.Endpoints;


var builder = WebApplication.CreateBuilder(args);

// Pasta física do projeto.
var projectRoot = builder.Environment.ContentRootPath;

// Pasta libs dentro do projeto.
var libsFolder = Path.Combine(projectRoot, "libs");

// Inicializa o carregamento dinâmico de DLLs e namespaces.
DynamicAssemblyRegistry.Initialize(libsFolder, typeof(Program).Assembly);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddElsa(elsa =>
{
    elsa.UseJavaScript();

    elsa.UseCSharp(options =>
    {
        foreach (var assembly in DynamicAssemblyRegistry.AllAssemblies)
            options.Assemblies.Add(assembly);

        foreach (var ns in DynamicAssemblyRegistry.AllNamespaces)
            options.Namespaces.Add(ns);
    });

    elsa.AddActivity<SomarActivity>();
    elsa.AddActivity<CalculoActivity>();
    elsa.AddActivity<WaitForSignalActivity>();
    elsa.AddActivitiesFrom<Program>();
});

var app = builder.Build();

Console.WriteLine("SomarActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<SomarActivity>());
Console.WriteLine("CalculoActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<CalculoActivity>());
Console.WriteLine("WaitForSignalActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<WaitForSignalActivity>());

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WorkflowManager.Client._Imports).Assembly);

app.MapGet("/debug-libs", () =>
{
    return Results.Ok(new
    {
        ProjectRoot = projectRoot,
        LibsFolder = libsFolder,
        LibsExists = Directory.Exists(libsFolder),
        DllFiles = Directory.Exists(libsFolder)
            ? Directory.GetFiles(libsFolder, "*.dll", SearchOption.AllDirectories)
                .OrderBy(Path.GetFileName)
                .ToArray()
            : Array.Empty<string>()
    });
});

app.MapGet("/debug-all-types", () =>
{
    var catalog = ReflectionCatalog.GetAssembliesAndTypes(DynamicAssemblyRegistry.AllAssemblies);

    return Results.Ok(new
    {
        AssemblyCount = catalog.Count,
        catalog
    });
});

/*
app.MapGet("/debug-activities", async (IActivityRegistry registry) =>
{
    var descriptors = await registry.ListAsync();

    return Results.Ok(
        descriptors
            .Select(x => new
            {
                x.TypeName,
                x.Name,
                x.DisplayName,
                x.Category
            })
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
            .ToArray()
    );
});
*/

app.MapGet("/debug-loaded-assemblies", () =>
{
    return Results.Ok(new
    {
        LibsFolder = libsFolder,
        LoadedAssemblyCount = DynamicAssemblyRegistry.AllAssemblies.Count,
        LoadedAssemblies = DynamicAssemblyRegistry.AllAssemblies
            .Select(a => new
            {
                Name = a.GetName().Name,
                FullName = a.FullName,
                Location = SafeGetLocation(a)
            })
            .OrderBy(x => x.Name)
            .ToArray()
    });
});

app.MapGet("/debug-loaded-namespaces", () =>
{
    return Results.Ok(new
    {
        NamespaceCount = DynamicAssemblyRegistry.AllNamespaces.Count,
        Namespaces = DynamicAssemblyRegistry.AllNamespaces
            .OrderBy(x => x)
            .ToArray()
    });
});

app.MapGet("/debug-elsa-types", () =>
{
    var result = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a =>
        {
            var name = a.GetName().Name ?? "";
            return name.StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase);
        })
        .OrderBy(a => a.GetName().Name)
        .Select(a =>
        {
            Type[] types;
            try
            {
                types = a.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            return new
            {
                Assembly = a.GetName().Name,
                Types = types
                    .Where(t => t.Name.Contains("Sequence", StringComparison.OrdinalIgnoreCase)
                             || t.FullName!.Contains("SetVariable", StringComparison.OrdinalIgnoreCase)
                             || t.FullName!.Contains("WriteLine", StringComparison.OrdinalIgnoreCase))
                    .Select(t => new
                    {
                        t.FullName,
                        t.Namespace,
                        t.Name
                    })
                    .OrderBy(t => t.FullName)
                    .ToArray()
            };
        })
        .Where(x => x.Types.Any())
        .ToArray();

    return Results.Ok(result);
});

app.MapGet("/debug-find-types/{name}", (string name) =>
{
    var types = DynamicAssemblyRegistry.FindTypesByName(name)
        .Select(t => new
        {
            t.FullName,
            t.Name,
            Namespace = t.Namespace,
            Assembly = t.Assembly.GetName().Name
        })
        .ToArray();

    return Results.Ok(types);
});

app.MapBookmarkEndpoints();

app.Run();

static string SafeGetLocation(Assembly assembly)
{
    try
    {
        return assembly.Location;
    }
    catch
    {
        return string.Empty;
    }
}


/*
namespace WorkflowManager.Activities
{
    [Activity("BionicCrow", "Foundation", "Invoca um método público de uma classe carregada dinamicamente")]
    public class InvokeFoundationMethodActivity : CodeActivity
    {
        [Input(Description = "Nome completo do tipo. Ex: BionicCrow.Foundation.Core.TestClass2")]
        public Input<string> TypeName { get; set; } = default!;

        [Input(Description = "Nome do método público")]
        public Input<string> MethodName { get; set; } = default!;

        [Input(Description = "Argumentos do construtor em JSON. Ex: [\"Miguel\",1]")]
        public Input<string?> ConstructorArgsJson { get; set; } = default!;

        [Input(Description = "Argumentos do método em JSON. Ex: [\"Joao\",2]")]
        public Input<string?> MethodArgsJson { get; set; } = default!;

        [Output(Description = "Resultado em texto/JSON")]
        public Output<string> Result { get; set; } = default!;

        protected override void Execute(ActivityExecutionContext context)
        {
            var typeName = TypeName.Get(context);
            var methodName = MethodName.Get(context);
            var ctorJson = ConstructorArgsJson.Get(context);
            var methodJson = MethodArgsJson.Get(context);

            var ctorArgs = ParseJsonArray(ctorJson);
            var methodArgs = ParseJsonArray(methodJson);

            var result = ReflectionInvoker.InvokeMethod(
                fullTypeName: typeName,
                methodName: methodName,
                constructorArgs: ctorArgs,
                methodArgs: methodArgs);

            var text = result switch
            {
                null => "",
                string s => s,
                _ => JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };

            Result.Set(context, text);
        }

        private static object?[] ParseJsonArray(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<object?>();

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("O valor informado deve ser um JSON array.");

            var list = new List<object?>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                list.Add(item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Number when item.TryGetInt32(out var i) => i,
                    JsonValueKind.Number when item.TryGetInt64(out var l) => l,
                    JsonValueKind.Number when item.TryGetDouble(out var d) => d,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => item.ToString()
                });
            }

            return list.ToArray();
        }
    }
}
*/
public static class DynamicAssemblyRegistry
{
    private static readonly object Sync = new();
    private static bool _initialized;
    private static string? _libsFolder;

    private static readonly List<Assembly> _assemblies = new();
    private static readonly HashSet<string> _namespaces = new(StringComparer.Ordinal);
    private static readonly List<string> _dllPaths = new();

    public static IReadOnlyList<Assembly> AllAssemblies => _assemblies;
    public static IReadOnlyCollection<string> AllNamespaces => _namespaces;
    public static IReadOnlyList<string> AllDllPaths => _dllPaths;

    public static void Initialize(string libsFolder, params Assembly[] extraAssemblies)
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            _initialized = true;
            _libsFolder = libsFolder;

            Console.WriteLine($"[DynamicAssemblyRegistry] Libs folder: {_libsFolder}");

            AssemblyLoadContext.Default.Resolving += ResolveFromLibsFolder;

            // Carrega todas as DLLs encontradas em libs.
            if (Directory.Exists(_libsFolder))
            {
                var dllFiles = Directory
                    .GetFiles(_libsFolder, "*.dll", SearchOption.AllDirectories)
                    .OrderBy(Path.GetFileName)
                    .ToList();

                Console.WriteLine("[DynamicAssemblyRegistry] DLLs encontradas:");
                foreach (var dll in dllFiles)
                {
                    Console.WriteLine(" - " + dll);
                    _dllPaths.Add(dll);
                }

                foreach (var dllPath in dllFiles)
                {
                    TryLoadAssemblyFromPath(dllPath);
                }
            }
            else
            {
                Console.WriteLine($"[DynamicAssemblyRegistry] Pasta libs não encontrada: {_libsFolder}");
            }

            // Registra assemblies extras, como a própria assembly do host.
            foreach (var assembly in extraAssemblies.Distinct())
                RegisterAssembly(assembly);

            Console.WriteLine($"[DynamicAssemblyRegistry] Assemblies registradas: {_assemblies.Count}");
            Console.WriteLine($"[DynamicAssemblyRegistry] Namespaces descobertos: {_namespaces.Count}");
        }
    }

    public static IReadOnlyList<Type> FindTypesByName(string typeName)
    {
        return DynamicAssemblyRegistry.AllAssemblies
            .SelectMany(GetLoadableTypes)
            .Where(t => t.IsClass && string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.FullName)
            .ToList();

        static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
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
    }

    public static Type? FindType(string fullTypeName)
    {
        foreach (var assembly in _assemblies)
        {
            try
            {
                var type = assembly.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
                if (type != null)
                    return type;
            }
            catch
            {
                // Ignora e continua procurando.
            }
        }

        return null;
    }

    private static Assembly? ResolveFromLibsFolder(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(_libsFolder) || !Directory.Exists(_libsFolder))
            return null;

        try
        {
            var dependencyPath = Directory
                .GetFiles(_libsFolder, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(dependencyPath))
                return null;

            var fullPath = Path.GetFullPath(dependencyPath);

            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a =>
                {
                    try
                    {
                        return !string.IsNullOrWhiteSpace(a.Location) &&
                               string.Equals(Path.GetFullPath(a.Location), fullPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (alreadyLoaded != null)
                return alreadyLoaded;

            var loaded = context.LoadFromAssemblyPath(fullPath);
            RegisterAssembly(loaded);
            return loaded;
        }
        catch
        {
            return null;
        }
    }

    private static void TryLoadAssemblyFromPath(string dllPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(dllPath);

            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a =>
                {
                    try
                    {
                        return !string.IsNullOrWhiteSpace(a.Location) &&
                               string.Equals(Path.GetFullPath(a.Location), fullPath, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            Assembly assembly;

            if (alreadyLoaded != null)
            {
                assembly = alreadyLoaded;
                Console.WriteLine($"[DynamicAssemblyRegistry] Já carregada: {assembly.GetName().Name}");
            }
            else
            {
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                Console.WriteLine($"[DynamicAssemblyRegistry] Carregada: {assembly.GetName().Name}");
            }

            RegisterAssembly(assembly);
        }
        catch (BadImageFormatException)
        {
            Console.WriteLine($"[DynamicAssemblyRegistry] Ignorando arquivo não gerenciado/inválido: {dllPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DynamicAssemblyRegistry] Erro ao carregar {dllPath}: {ex.Message}");
        }
    }

    private static void RegisterAssembly(Assembly assembly)
    {
        if (_assemblies.Any(x => string.Equals(x.FullName, assembly.FullName, StringComparison.OrdinalIgnoreCase)))
            return;

        _assemblies.Add(assembly);

        foreach (var ns in GetNamespacesFromAssembly(assembly))
            _namespaces.Add(ns);
    }

    private static IEnumerable<string> GetNamespacesFromAssembly(Assembly assembly)
    {
        return GetLoadableTypes(assembly)
            .Where(t => !string.IsNullOrWhiteSpace(t.Namespace))
            .Select(t => t.Namespace!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x);
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
}


public static class ReflectionCatalog
{
    public static IReadOnlyList<AssemblyInfoDto> GetAssembliesAndTypes(IEnumerable<Assembly> assemblies)
    {
        var result = new List<AssemblyInfoDto>();

        foreach (var assembly in assemblies)
        {
            var types = GetLoadableTypes(assembly)
                .Where(t => t.IsClass)
                .OrderBy(t => t.FullName)
                .Select(t => new TypeInfoDto
                {
                    FullName = t.FullName ?? "",
                    Name = t.Name,
                    Namespace = t.Namespace ?? "",
                    IsAbstract = t.IsAbstract,
                    Methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Where(m => !m.IsSpecialName)
                        .OrderBy(m => m.Name)
                        .Select(m => new MethodInfoDto
                        {
                            Name = m.Name,
                            ReturnType = m.ReturnType.FullName ?? m.ReturnType.Name,
                            Parameters = m.GetParameters()
                                .Select(p => $"{p.ParameterType.Name} {p.Name}")
                                .ToArray()
                        })
                        .ToArray(),
                    Properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(p => $"{p.PropertyType.Name} {p.Name}")
                        .ToArray(),
                    Constructors = t.GetConstructors()
                        .Select(c => string.Join(", ", c.GetParameters()
                            .Select(p => $"{p.ParameterType.Name} {p.Name}")))
                        .ToArray()
                })
                .ToArray();

            result.Add(new AssemblyInfoDto
            {
                Name = assembly.GetName().Name ?? "",
                FullName = assembly.FullName ?? "",
                Types = types
            });
        }

        return result;
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
}

public sealed class AssemblyInfoDto
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public TypeInfoDto[] Types { get; set; } = Array.Empty<TypeInfoDto>();
}

public sealed class TypeInfoDto
{
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public bool IsAbstract { get; set; }
    public MethodInfoDto[] Methods { get; set; } = Array.Empty<MethodInfoDto>();
    public string[] Properties { get; set; } = Array.Empty<string>();
    public string[] Constructors { get; set; } = Array.Empty<string>();
}

public sealed class MethodInfoDto
{
    public string Name { get; set; } = "";
    public string ReturnType { get; set; } = "";
    public string[] Parameters { get; set; } = Array.Empty<string>();
}

public static class ReflectionInvoker
{
    public static object? InvokeMethod(
        string fullTypeName,
        string methodName,
        object?[]? constructorArgs = null,
        object?[]? methodArgs = null)
    {
        var type = DynamicAssemblyRegistry.FindType(fullTypeName);

        if (type == null)
            throw new InvalidOperationException($"Tipo não encontrado: {fullTypeName}");

        object? instance = null;

        if (!type.IsAbstract && !type.IsInterface)
        {
            instance = Activator.CreateInstance(type, constructorArgs ?? Array.Empty<object?>());
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToList();

        if (!methods.Any())
            throw new InvalidOperationException($"Método não encontrado: {methodName}");

        var method = methods.FirstOrDefault(m => m.GetParameters().Length == (methodArgs?.Length ?? 0));

        if (method == null)
            throw new InvalidOperationException($"Nenhuma sobrecarga compatível encontrada para {methodName}");

        return method.Invoke(method.IsStatic ? null : instance, methodArgs ?? Array.Empty<object?>());
    }
}