using Elsa.Extensions;
using WorkflowManager.Client.Pages;
using WorkflowManager.Components;
using System.Reflection;
using System.Runtime.Loader;

var builder = WebApplication.CreateBuilder(args);

// Pasta física do projeto.
var projectRoot = builder.Environment.ContentRootPath;

// Pasta libs dentro do projeto.
var libsFolder = Path.Combine(projectRoot, "libs");

// Caminho da DLL principal.
var bionicDllPath = Path.Combine(libsFolder, "BionicCrow.Foundation.dll");

Assembly? bionicAssembly = null;
string? loadedPath = null;

// Resolver dependências da pasta libs.
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    var dependencyPath = Path.Combine(libsFolder, $"{assemblyName.Name}.dll");

    if (!File.Exists(dependencyPath))
        return null;

    return context.LoadFromAssemblyPath(dependencyPath);
};

try
{
    Console.WriteLine("ContentRootPath: " + projectRoot);
    Console.WriteLine("LibsFolder: " + libsFolder);
    Console.WriteLine("DLL principal: " + bionicDllPath);
    Console.WriteLine("DLL existe? " + File.Exists(bionicDllPath));

    if (File.Exists(bionicDllPath))
    {
        bionicAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(bionicDllPath);
        loadedPath = bionicDllPath;
        Console.WriteLine("DLL carregada com sucesso.");
    }
    else
    {
        Console.WriteLine("DLL não encontrada: " + bionicDllPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine("Erro ao carregar DLL: " + ex);
}

if (bionicAssembly == null)
    Console.WriteLine("Nenhum caminho funcionou para carregar BionicCrow.Foundation.dll");


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddElsa(elsa =>
{
    elsa.UseJavaScript();

    elsa.UseCSharp(options =>
    {
        if (bionicAssembly != null)
            options.Assemblies.Add(bionicAssembly);

        options.Namespaces.Add("BionicCrow.Foundation.Core");
        options.Namespaces.Add("BionicCrow.Foundation.System");
        options.Namespaces.Add("BionicCrow.Foundation.Resolved");
        options.Namespaces.Add("BionicCrow.Foundation.Interfaces");
        options.Namespaces.Add("BionicCrow.Foundation.DTO");
        options.Namespaces.Add("BionicCrow.Foundation.Enums");

        options.AppendScript("""
            string LerTestClass2Reflection(string nome, int id)
            {
                System.Reflection.Assembly? asm = null;

                foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "BionicCrow.Foundation")
                    {
                        asm = a;
                        break;
                    }
                }

                if (asm == null)
                    throw new System.Exception("Assembly BionicCrow.Foundation não encontrada no AppDomain.");

                var type = asm.GetType("BionicCrow.Foundation.Core.TestClass2");

                if (type == null)
                    throw new System.Exception("Tipo BionicCrow.Foundation.Core.TestClass2 não encontrado.");

                var obj = System.Activator.CreateInstance(type, nome, id);

                if (obj == null)
                    throw new System.Exception("Não foi possível criar a instância de TestClass2.");

                var nomeValue = type.GetProperty("Nome")?.GetValue(obj)?.ToString() ?? "";
                var idValue = type.GetProperty("Id")?.GetValue(obj)?.ToString() ?? "";

                var metodoResumo = type.GetMethod("Resumo");
                var resumoValue = metodoResumo?.Invoke(obj, null)?.ToString() ?? "";

                return $"Id: {idValue} | Nome: {nomeValue} | Resumo: {resumoValue}";
            }
            """);
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
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WorkflowManager.Client._Imports).Assembly);

/*
app.MapGet("/test-bionic", () =>
{
    try
    {
        var result = new
        {
            ResolvedEntityType = typeof(ResolvedEntity).FullName,
            ResolvedLibraryType = typeof(ResolvedLibrary).FullName,
            SystemObjectsType = typeof(SystemObjects).FullName,
            ResolvedEntityConstructors = typeof(ResolvedEntity)
                .GetConstructors()
                .Select(c => c.ToString())
                .ToArray(),
            ResolvedLibraryConstructors = typeof(ResolvedLibrary)
                .GetConstructors()
                .Select(c => c.ToString())
                .ToArray()
        };

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});
*/
app.MapGet("/debug-bionic-path", () =>
{
    return Results.Ok(new
    {
        ContentRootPath = projectRoot,
        LibsFolder = libsFolder,
        MainDllPath = bionicDllPath,
        MainDllExists = File.Exists(bionicDllPath),
        AssemblyLoaded = bionicAssembly != null,
        LoadedAssembly = bionicAssembly?.FullName,
        LoadedFrom = loadedPath
    });
});


// Endpoint de teste para confirmar que a DLL carregou.
app.MapGet("/testclass-reflection", () =>
{
    if (bionicAssembly == null)
        return Results.Problem("A DLL não foi carregada.");

    var testClassType = bionicAssembly.GetType("BionicCrow.Foundation.Core.TestClass");

    if (testClassType == null)
        return Results.Problem("O tipo BionicCrow.Foundation.Core.TestClass não foi encontrado.");

    var instance = Activator.CreateInstance(
        testClassType,
        "Teste via Reflection",
        "Descrição criada via DLL");

    if (instance == null)
        return Results.Problem("Não foi possível criar a instância de TestClass.");

    var nome = testClassType.GetProperty("Nome")?.GetValue(instance)?.ToString();
    var descricao = testClassType.GetProperty("Descricao")?.GetValue(instance)?.ToString();

    return Results.Ok(new
    {
        Tipo = testClassType.FullName,
        Nome = nome,
        Descricao = descricao,
        LoadedFrom = loadedPath
    });
});

app.MapGet("/debug-bionic-files", () =>
{
    var projectLibs = Path.Combine(projectRoot, "libs");
    var bionicLibs = Path.Combine(projectRoot, "libs", "BionicCrow");

    return Results.Ok(new
    {
        ProjectRoot = projectRoot,
        ProjectLibsExists = Directory.Exists(projectLibs),
        ProjectLibsFiles = Directory.Exists(projectLibs)
            ? Directory.GetFiles(projectLibs, "*.*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray()
            : Array.Empty<string>(),

        BionicLibsExists = Directory.Exists(bionicLibs),
        BionicLibsFiles = Directory.Exists(bionicLibs)
            ? Directory.GetFiles(bionicLibs, "*.*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray()
            : Array.Empty<string>()
    });
});

app.MapGet("/testclass-type", () =>
{
    if (bionicAssembly == null)
        return Results.Problem("A DLL não foi carregada.");

    var type = bionicAssembly.GetType("BionicCrow.Foundation.Core.TestClass");

    return Results.Ok(new
    {
        Found = type != null,
        FullName = type?.FullName,
        Namespace = type?.Namespace,
        Assembly = type?.Assembly.FullName
    });
});

app.MapGet("/testclass-properties", () =>
{
    if (bionicAssembly == null)
        return Results.Problem("A DLL não foi carregada.");

    var type = bionicAssembly.GetType("BionicCrow.Foundation.Core.TestClass");
    if (type == null)
        return Results.Problem("Tipo não encontrado.");

    var props = type.GetProperties()
        .Select(p => new
        {
            p.Name,
            PropertyType = p.PropertyType.FullName
        });

    return Results.Ok(props);
});

app.MapGet("/testclass-constructors", () =>
{
    if (bionicAssembly == null)
        return Results.Problem("A DLL não foi carregada.");

    var type = bionicAssembly.GetType("BionicCrow.Foundation.Core.TestClass");
    if (type == null)
        return Results.Problem("Tipo não encontrado.");

    var ctors = type.GetConstructors()
        .Select(c => c.ToString())
        .ToArray();

    return Results.Ok(ctors);
});



app.Run();
