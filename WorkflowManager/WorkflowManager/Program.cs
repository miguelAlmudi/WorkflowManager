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
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.EntityFrameworkCore.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Components;
using WorkflowManager.Workflows;
using WorkflowManager.Services;


var builder = WebApplication.CreateBuilder(args);

// Pasta física do projeto.
var projectRoot = builder.Environment.ContentRootPath;

// Pasta libs dentro do projeto.
var libsFolder = Path.Combine(projectRoot, "libs");

var sqliteConnectionString = $"Data Source={Path.Combine(projectRoot, "elsa.db")}";

// Inicializa o carregamento dinâmico de DLLs e namespaces.
DynamicAssemblyRegistry.Initialize(libsFolder, typeof(Program).Assembly);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddScoped<HttpClient>(sp =>
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();

    return new HttpClient
    {
        BaseAddress = new Uri(navigationManager.BaseUri)
    };
});


builder.Services.AddElsa(elsa =>
{
    elsa.UseWorkflowManagement(management =>
    {
        management.UseEntityFrameworkCore(ef =>
        {
            ef.UseSqlite(sqliteConnectionString);
            ef.RunMigrations = true;
        });
    });

    elsa.UseWorkflowRuntime(runtime =>
    {
        runtime.UseEntityFrameworkCore(ef =>
        {
            ef.UseSqlite(sqliteConnectionString);
            ef.RunMigrations = true;
        });
    });


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
    elsa.AddActivity<WaitForObjectFieldsActivity>();
    elsa.AddWorkflow<ObjectFieldsWorkflowMotor>();
    elsa.AddWorkflow<ObjectFieldsWorkflowSensor>();
    elsa.AddWorkflow<ObjectFieldsWorkflowControlador>();

    elsa.AddActivity<WaitForSumValuesActivity>();
    elsa.AddWorkflow<SumWorkflow>();

    elsa.AddActivitiesFrom<Program>();

    elsa.UseWorkflowManagement(management =>
    {
        var dynamicActivityTypes = DynamicAssemblyRegistry.FindActivityTypes()
            .Where(t => t.Assembly != typeof(Program).Assembly)
            .ToList();

        Console.WriteLine("=================================");
        Console.WriteLine("[DynamicActivityRegistration] Activities encontradas via Reflection:");

        foreach (var activityType in dynamicActivityTypes)
        {
            Console.WriteLine($"CLR Type: {activityType.FullName}");
            Console.WriteLine($"Elsa TypeName: {ActivityTypeNameHelper.GenerateTypeName(activityType)}");
        }

        Console.WriteLine($"Total: {dynamicActivityTypes.Count}");
        Console.WriteLine("=================================");

        management.AddActivities(dynamicActivityTypes);
    });
});

builder.Services.AddScoped<WorkflowExecutionService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var registry = scope.ServiceProvider.GetRequiredService<IActivityRegistry>();

    var dynamicActivityTypes = DynamicAssemblyRegistry.FindActivityTypes()
        .Where(t => t.Assembly != typeof(Program).Assembly)
        .ToList();

    Console.WriteLine("=================================");
    Console.WriteLine("[Registro dinâmico de activities no ActivityRegistry]");

    foreach (var activityType in dynamicActivityTypes)
    {
        var generatedTypeName = ActivityTypeNameHelper.GenerateTypeName(activityType);

        Console.WriteLine($"Tipo CLR: {activityType.FullName}");
        Console.WriteLine($"TypeName Elsa: {generatedTypeName}");
    }

    registry.RegisterAsync(dynamicActivityTypes, CancellationToken.None)
        .GetAwaiter()
        .GetResult();

    Console.WriteLine("=================================");
}

Console.WriteLine("SomarActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<SomarActivity>());
Console.WriteLine("CalculoActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<CalculoActivity>());
Console.WriteLine("WaitForSignalActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<WaitForSignalActivity>());
Console.WriteLine("WaitForObjectFieldsActivity TypeName = " + ActivityTypeNameHelper.GenerateTypeName<WaitForObjectFieldsActivity>());

using (var scope = app.Services.CreateScope())
{
    var registry = scope.ServiceProvider.GetRequiredService<IActivityRegistry>();

    var dynamicActivityTypes = DynamicAssemblyRegistry.FindActivityTypes()
        .Where(t => t.Assembly != typeof(Program).Assembly)
        .ToList();

    Console.WriteLine("=================================");
    Console.WriteLine("[ActivityRegistry] Registro forçado de activities dinâmicas");

    foreach (var activityType in dynamicActivityTypes)
    {
        Console.WriteLine($"Registrando: {activityType.FullName}");
        Console.WriteLine($"Elsa TypeName: {ActivityTypeNameHelper.GenerateTypeName(activityType)}");
    }

    registry.RegisterAsync(dynamicActivityTypes, CancellationToken.None)
        .GetAwaiter()
        .GetResult();

    Console.WriteLine($"Total registrado: {dynamicActivityTypes.Count}");
    Console.WriteLine("=================================");
}


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

app.MapGet("/debug-dynamic-activity-types", () =>
{
    var activities = DynamicAssemblyRegistry.FindActivityTypeInfos();

    return Results.Ok(new
    {
        Count = activities.Count,
        Activities = activities
    });
});

app.MapGet("/debug-activity-registry", (IActivityRegistry registry) =>
{
    var descriptors = registry.ListAll()
        .Select(x => new
        {
            x.TypeName,
            x.Version,
            x.Name,
            x.DisplayName,
            x.Category,
            x.Namespace
        })
        .OrderBy(x => x.TypeName)
        .ThenBy(x => x.Version)
        .ToArray();

    return Results.Ok(new
    {
        Count = descriptors.Length,
        Activities = descriptors
    });
});

app.MapGet("/debug-workflow-services", (IServiceProvider services) =>
{
    var serviceProviderType = services.GetType();

    var assemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => (a.GetName().Name ?? "").StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    var types = assemblies
        .SelectMany(a =>
        {
            try
            {
                return a.GetTypes();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        })
        .Where(t =>
            t.Name.Contains("WorkflowDefinition", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("WorkflowPublisher", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("WorkflowRegistry", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("WorkflowStore", StringComparison.OrdinalIgnoreCase))
        .Select(t => new
        {
            t.FullName,
            t.Name,
            Assembly = t.Assembly.GetName().Name
        })
        .OrderBy(x => x.FullName)
        .ToArray();

    return Results.Ok(types);
});


app.MapGet("/debug-activities", async (IActivityRegistry registry) =>
{
    var descriptors = registry.ListAll()
        .Where(x =>
            x.TypeName.Contains("Hello", StringComparison.OrdinalIgnoreCase) ||
            x.TypeName.Contains("Dynamic", StringComparison.OrdinalIgnoreCase) ||
            x.TypeName.Contains("Object", StringComparison.OrdinalIgnoreCase) ||
            x.TypeName.Contains("WriteLine", StringComparison.OrdinalIgnoreCase))
        .Select(x => new
        {
            x.TypeName,
            x.Version,
            x.Name,
            x.DisplayName,
            x.Category,
            x.Namespace
        })
        .OrderBy(x => x.TypeName)
        .ThenBy(x => x.Version)
        .ToArray();

    return Results.Ok(descriptors);
});

app.MapGet("/debug-dynamic-activity-types", () =>
{
    var activityTypes = DynamicAssemblyRegistry.FindActivityTypes()
        .Select(t => new
        {
            FullName = t.FullName,
            Name = t.Name,
            Namespace = t.Namespace,
            Assembly = t.Assembly.GetName().Name,
            GeneratedTypeName = ActivityTypeNameHelper.GenerateTypeName(t)
        })
        .OrderBy(x => x.FullName)
        .ToArray();

    return Results.Ok(activityTypes);
});

app.MapGet("/debug-foundation-types", () =>
{
    var result = DynamicAssemblyRegistry.AllAssemblies
        .Where(a => string.Equals(a.GetName().Name, "BionicCrow.Foundation", StringComparison.OrdinalIgnoreCase))
        .SelectMany(a =>
        {
            try
            {
                return a.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        })
        .Where(t => t.IsClass)
        .OrderBy(t => t.FullName)
        .Select(t => new
        {
            t.FullName,
            t.Name,
            Namespace = t.Namespace,
            Assembly = t.Assembly.GetName().Name,
            IsActivity = typeof(IActivity).IsAssignableFrom(t),
            ElsaTypeName = typeof(IActivity).IsAssignableFrom(t)
                ? ActivityTypeNameHelper.GenerateTypeName(t)
                : null,
            BaseType = t.BaseType?.FullName,
            Methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => new
                {
                    m.Name,
                    ReturnType = m.ReturnType.FullName ?? m.ReturnType.Name,
                    Parameters = m.GetParameters()
                        .Select(p => new
                        {
                            p.Name,
                            Type = p.ParameterType.FullName ?? p.ParameterType.Name
                        })
                        .ToArray()
                })
                .OrderBy(m => m.Name)
                .ToArray()
        })
        .ToArray();

    return Results.Ok(new
    {
        Count = result.Length,
        Types = result
    });
});


app.MapGet("/debug-type-methods/{typeName}", (string typeName) =>
{
    var types = DynamicAssemblyRegistry.AllAssemblies
        .SelectMany(a =>
        {
            try
            {
                return a.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        })
        .Where(t =>
            t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
            (t.FullName?.Contains(typeName, StringComparison.OrdinalIgnoreCase) ?? false))
        .Select(t => new
        {
            t.FullName,
            t.Name,
            Namespace = t.Namespace,
            Assembly = t.Assembly.GetName().Name,
            IsActivity = typeof(IActivity).IsAssignableFrom(t),
            ElsaTypeName = typeof(IActivity).IsAssignableFrom(t)
                ? ActivityTypeNameHelper.GenerateTypeName(t)
                : null,
            Methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => new
                {
                    m.Name,
                    ReturnType = m.ReturnType.FullName ?? m.ReturnType.Name,
                    Parameters = m.GetParameters()
                        .Select(p => new
                        {
                            p.Name,
                            Type = p.ParameterType.FullName ?? p.ParameterType.Name
                        })
                        .ToArray()
                })
                .OrderBy(m => m.Name)
                .ToArray()
        })
        .OrderBy(x => x.FullName)
        .ToArray();

    return Results.Ok(types);
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

app.MapGet("/debug-sqlite", async () =>
{
    var dbPath = Path.Combine(projectRoot, "elsa.db");
    var connectionString = $"Data Source={dbPath}";

    var result = new List<object>();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var tablesCommand = connection.CreateCommand();
    tablesCommand.CommandText = """
        SELECT name
        FROM sqlite_master
        WHERE type = 'table'
        ORDER BY name
    """;

    var tableNames = new List<string>();

    await using (var reader = await tablesCommand.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            tableNames.Add(reader.GetString(0));
    }

    foreach (var table in tableNames)
    {
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM [{table}]";

        var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync());

        result.Add(new
        {
            Table = table,
            Count = count
        });
    }

    return Results.Ok(new
    {
        Database = dbPath,
        Tables = result
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
app.MapObjectWorkflowEndpoints();
app.MapSumWorkflowEndpoints();
app.MapGenericWorkflowEndpoints();
app.MapTriggerEndpoints();

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

    public static IReadOnlyList<Type> FindActivityTypes()
    {
        return _assemblies
            .SelectMany(GetLoadableTypes)
            .Where(IsConcreteActivityType)
            .OrderBy(t => t.FullName)
            .ToList();
    }

    public static IReadOnlyList<ActivityReflectionInfo> FindActivityTypeInfos()
    {
        return FindActivityTypes()
            .Select(t => new ActivityReflectionInfo
            {
                FullName = t.FullName ?? "",
                Name = t.Name,
                Namespace = t.Namespace ?? "",
                AssemblyName = t.Assembly.GetName().Name ?? "",
                AssemblyFullName = t.Assembly.FullName ?? "",
                Location = SafeLocation(t.Assembly),
                ElsaTypeName = ActivityTypeNameHelper.GenerateTypeName(t),
                IsActivity = true,
                IsAbstract = t.IsAbstract,
                IsGenericType = t.IsGenericType,
                BaseType = t.BaseType?.FullName,
                Inputs = GetPropertiesWithAttributeName(t, "InputAttribute"),
                Outputs = GetPropertiesWithAttributeName(t, "OutputAttribute")
            })
            .ToList();
    }

    private static bool IsConcreteActivityType(Type type)
    {
        return typeof(IActivity).IsAssignableFrom(type)
            && type.IsClass
            && !type.IsAbstract
            && !type.IsInterface
            && !type.IsGenericType;
    }

    private static string SafeLocation(Assembly assembly)
    {
        try
        {
            return assembly.Location;
        }
        catch
        {
            return "";
        }
    }

    private static ActivityPropertyInfo[] GetPropertiesWithAttributeName(Type type, string attributeName)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttributes(inherit: true)
                .Any(a => a.GetType().Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new ActivityPropertyInfo
            {
                Name = p.Name,
                PropertyType = p.PropertyType.FullName ?? p.PropertyType.Name
            })
            .OrderBy(p => p.Name)
            .ToArray();
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

public sealed class ActivityReflectionInfo
{
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string AssemblyFullName { get; set; } = "";
    public string Location { get; set; } = "";
    public string ElsaTypeName { get; set; } = "";
    public bool IsActivity { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsGenericType { get; set; }
    public string? BaseType { get; set; }
    public ActivityPropertyInfo[] Inputs { get; set; } = Array.Empty<ActivityPropertyInfo>();
    public ActivityPropertyInfo[] Outputs { get; set; } = Array.Empty<ActivityPropertyInfo>();
}

public sealed class ActivityPropertyInfo
{
    public string Name { get; set; } = "";
    public string PropertyType { get; set; } = "";
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