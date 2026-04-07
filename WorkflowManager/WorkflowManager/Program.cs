using Elsa.Extensions;
using WorkflowManager.Client.Pages;
using WorkflowManager.Components;
using BionicCrow.Foundation.System;
using BionicCrow.Foundation.Resolved;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddElsa(elsa =>
{
    elsa.UseCSharp(options =>
    {
        options.Assemblies.Add(typeof(SystemObjects).Assembly);
        options.Namespaces.Add("BionicCrow.Foundation.System");
        options.Namespaces.Add("BionicCrow.Foundation.Resolved");
        options.Namespaces.Add("BionicCrow.Foundation.Interfaces");
        options.Namespaces.Add("BionicCrow.Foundation.DTO");
        options.Namespaces.Add("BionicCrow.Foundation.Enums");
        options.AppendScript("""
            string ProbeResolvedEntity() => typeof(ResolvedEntity).FullName!;
            string ProbeResolvedLibrary() => typeof(ResolvedLibrary).FullName!;
        """);
    });
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

app.Run();
