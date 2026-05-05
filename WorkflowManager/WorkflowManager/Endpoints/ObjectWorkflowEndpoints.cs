using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Microsoft.Data.Sqlite;
using WorkflowManager.Activities;
using Elsa.Workflows.Runtime.Messages;

namespace WorkflowManager.Endpoints;

public static class ObjectWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapObjectWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/object-workflow/start", async (
    ObjectWorkflowStartRequest request,
    IWorkflowRuntime workflowRuntime) =>
        {
            var objectId = string.IsNullOrWhiteSpace(request.ObjectId)
                ? "objeto-001"
                : request.ObjectId;

            var definitionId = objectId switch
            {
                "objeto-001" => "ObjectFieldsWorkflowMotor",
                "objeto-002" => "ObjectFieldsWorkflowSensor",
                "objeto-003" => "ObjectFieldsWorkflowControlador",
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(definitionId))
            {
                return Results.BadRequest(new
                {
                    message = $"Não existe workflow de teste registrado para o objeto '{objectId}'.",
                    objectId
                });
            }

            // Limpa sinais antigos em memória antes de iniciar um novo teste.
            WaitForObjectFieldsActivity.ClearSignals(objectId);

            var client = await workflowRuntime.CreateClientAsync();

            var result = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId),
                CorrelationId = objectId
            });

            return Results.Ok(new
            {
                message = "Workflow iniciado pelo runtime.",
                objectId,
                definitionId,
                bookmarkName = $"ObjectFieldChanged:{objectId}",
                result
            });
        });

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

            var message = result.Bookmarks?.Any() == true
                ? "Workflow ainda está aguardando outro sinal."
                : "Workflow finalizado com sucesso.";

            return Results.Ok(new
            {
                objectId = request.ObjectId,
                bookmarkName,
                bookmarkId,
                field = normalizedField,
                value = request.Value,
                result,
                message
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

        var columns = await GetTableColumnsAsync(connection, "Bookmarks");

        var idColumn = PickFirstExistingColumn(columns, "BookmarkId", "Id");

        if (idColumn is null)
        {
            throw new InvalidOperationException(
                "Não foi encontrada coluna de ID na tabela Bookmarks. Colunas encontradas: " +
                string.Join(", ", columns));
        }

        var orderColumn = PickFirstExistingColumn(columns, "CreatedAt", "UpdatedAt", "Id");

        var command = connection.CreateCommand();

        command.CommandText = $"""
        SELECT [{idColumn}]
        FROM Bookmarks
        ORDER BY [{orderColumn}] DESC
        LIMIT 1
    """;

        var result = await command.ExecuteScalarAsync();

        return result?.ToString();
    }

    private static async Task<List<string>> GetTableColumnsAsync(
        SqliteConnection connection,
        string tableName)
    {
        var command = connection.CreateCommand();

        command.CommandText = $"PRAGMA table_info([{tableName}])";

        var columns = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var columnName = reader["name"]?.ToString();

            if (!string.IsNullOrWhiteSpace(columnName))
                columns.Add(columnName);
        }

        return columns;
    }

    private static string? PickFirstExistingColumn(
        List<string> columns,
        params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var match = columns.FirstOrDefault(x =>
                x.Equals(candidate, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        return null;
    }


    private static async Task<string> GetBookmarkIdColumnNameAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();

        command.CommandText = """
        PRAGMA table_info(Bookmarks)
    """;

        var columns = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var columnName = reader["name"]?.ToString();

            if (!string.IsNullOrWhiteSpace(columnName))
                columns.Add(columnName);
        }

        if (columns.Contains("BookmarkId", StringComparer.OrdinalIgnoreCase))
            return "BookmarkId";

        if (columns.Contains("Id", StringComparer.OrdinalIgnoreCase))
            return "Id";

        throw new InvalidOperationException(
            "Não foi encontrada uma coluna de identificador na tabela Bookmarks. Colunas encontradas: " +
            string.Join(", ", columns));
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


public sealed class ObjectWorkflowStartRequest
{
    public string ObjectId { get; set; } = "objeto-teste";
}