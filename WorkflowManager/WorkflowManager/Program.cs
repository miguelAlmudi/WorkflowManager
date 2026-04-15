using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using WorkflowManager.Client.Pages;
using WorkflowManager.Components;
using System.Reflection;
using System.Runtime.Loader;

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

    elsa.AddActivitiesFrom<Program>();
});

var app = builder.Build();

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

app.MapGet("/testclass2-reflection", () =>
{
    try
    {
        var result = TestClass2ReflectionHelper.Execute("Teste via endpoint", 123);

        return Results.Ok(new
        {
            Success = true,
            result.TypeFullName,
            result.AssemblyName,
            result.Nome,
            result.Id,
            result.Resumo
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});

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

    public static IReadOnlyList<Assembly> GetAssembliesWithActivities()
    {
        return _assemblies
            .Where(ContainsElsaActivities)
            .Distinct()
            .ToList();
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

    private static bool ContainsElsaActivities(Assembly assembly)
    {
        try
        {
            return GetLoadableTypes(assembly)
                .Any(t =>
                    t is { IsClass: true, IsAbstract: false } &&
                    typeof(IActivity).IsAssignableFrom(t));
        }
        catch
        {
            return false;
        }
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

public static class TestClass2ReflectionHelper
{
    public static TestClass2ReflectionResult Execute(string? nome, int id)
    {
        var type = DynamicAssemblyRegistry.FindType("BionicCrow.Foundation.Core.TestClass2");

        if (type == null)
            throw new InvalidOperationException(
                "O tipo 'BionicCrow.Foundation.Core.TestClass2' não foi encontrado entre as DLLs carregadas.");

        var instance = CreateInstance(type, nome, id);

        var nomeValue = type.GetProperty("Nome")?.GetValue(instance)?.ToString() ?? string.Empty;
        var idValue = type.GetProperty("Id")?.GetValue(instance)?.ToString() ?? id.ToString();

        var resumoMethod = type.GetMethod("Resumo", Type.EmptyTypes);
        var resumoValue = resumoMethod?.Invoke(instance, null)?.ToString() ?? string.Empty;

        return new TestClass2ReflectionResult
        {
            AssemblyName = type.Assembly.GetName().Name ?? string.Empty,
            TypeFullName = type.FullName ?? string.Empty,
            Nome = nomeValue,
            Id = idValue,
            Resumo = resumoValue
        };
    }

    private static object CreateInstance(Type type, string? nome, int id)
    {
        // Tenta construtor (string, int)
        var ctorStringInt = type.GetConstructor(new[] { typeof(string), typeof(int) });
        if (ctorStringInt != null)
            return ctorStringInt.Invoke(new object?[] { nome ?? string.Empty, id });

        // Tenta construtor padrão e depois preencher propriedades.
        var parameterlessCtor = type.GetConstructor(Type.EmptyTypes);
        if (parameterlessCtor != null)
        {
            var obj = parameterlessCtor.Invoke(null);

            var nomeProp = type.GetProperty("Nome");
            if (nomeProp?.CanWrite == true)
                nomeProp.SetValue(obj, nome ?? string.Empty);

            var idProp = type.GetProperty("Id");
            if (idProp?.CanWrite == true)
                idProp.SetValue(obj, id);

            return obj;
        }

        throw new InvalidOperationException(
            "Não foi possível instanciar TestClass2. " +
            "A classe precisa ter um construtor (string, int) ou um construtor vazio com propriedades Nome/Id graváveis.");
    }
}

public sealed class TestClass2ReflectionResult
{
    public string AssemblyName { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Resumo { get; set; } = string.Empty;
}

[Activity("BionicCrow", "Reflection", "Instancia BionicCrow.Foundation.Core.TestClass2 e retorna seu resumo")]
public class TestClass2Activity : CodeActivity
{
    [Input(Description = "Nome passado para TestClass2")]
    public Input<string> Nome { get; set; } = default!;

    [Input(Description = "Id passado para TestClass2")]
    public Input<int> Id { get; set; } = default!;

    [Output(Description = "Resumo gerado por TestClass2")]
    public Output<string> Resultado { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context)
    {
        var nome = Nome.Get(context);
        var id = Id.Get(context);

        var result = TestClass2ReflectionHelper.Execute(nome, id);

        Resultado.Set(context, result.Resumo);
    }
}