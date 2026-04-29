using Elsa.Workflows.Runtime;
using Microsoft.Data.Sqlite;

namespace WorkflowManager.Endpoints;

public static class ObjectWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapObjectWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/object-workflow/signal", async (
            ObjectFieldChangeRequest request,
            IWorkflowResumer workflowResumer,
            IWebHostEnvironment environment) =>
        {
            var normalizedField = NormalizeField(request.Field);

            if (normalizedField is null)
            {
                return Results.BadRequest(new
                {
                    message = "Campo inválido. Use 'name' ou 'description'."
                });
            }

            var bookmarkName = BuildBookmarkName(request.ObjectId);

            var bookmarkId = await FindBookmarkIdByNameAsync(
                environment.ContentRootPath,
                bookmarkName);

            if (string.IsNullOrWhiteSpace(bookmarkId))
            {
                return Results.NotFound(new
                {
                    objectId = request.ObjectId,
                    bookmarkName,
                    message = "Nenhum bookmark encontrado. Inicie o workflow primeiro."
                });
            }

            var input = new Dictionary<string, object>
            {
                ["ObjectId"] = request.ObjectId,
                ["Field"] = normalizedField,
                ["Value"] = request.Value ?? ""
            };

            var result = await workflowResumer.ResumeAsync(bookmarkId, input);

            return Results.Ok(new
            {
                objectId = request.ObjectId,
                bookmarkName,
                bookmarkId,
                field = normalizedField,
                value = request.Value,
                result,
                message = "Bookmark retomado com sucesso."
            });
        });

        return app;
    }

    private static async Task<string?> FindBookmarkIdByNameAsync(
        string contentRootPath,
        string bookmarkName)
    {
        var dbPath = Path.Combine(contentRootPath, "elsa.db");
        var connectionString = $"Data Source={dbPath}";

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();

        command.CommandText = """
            SELECT BookmarkId
            FROM Bookmarks
            WHERE SerializedOptions LIKE $bookmarkName
            ORDER BY CreatedAt DESC
            LIMIT 1
        """;

        command.Parameters.AddWithValue("$bookmarkName", $"%{bookmarkName}%");

        var result = await command.ExecuteScalarAsync();

        return result?.ToString();
    }

    private static string BuildBookmarkName(string objectId)
    {
        return $"ObjectFieldChanged:{objectId}";
    }

    private static string? NormalizeField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        field = field.Trim().ToLowerInvariant();

        return field switch
        {
            "name" or "nome" => "name",
            "description" or "descricao" or "descrição" => "description",
            _ => null
        };
    }
}

public sealed class ObjectFieldChangeRequest
{
    public string ObjectId { get; set; } = "objeto-teste";
    public string Field { get; set; } = "";
    public string? Value { get; set; }
}