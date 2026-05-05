using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Data.Sqlite;

namespace WorkflowManager.Endpoints;

public static class GenericWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapGenericWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/generic-workflow/start", async (
            GenericWorkflowStartRequest request,
    IWorkflowRuntime workflowRuntime) =>
        {
            if (string.IsNullOrWhiteSpace(request.DefinitionId))
            {
                return Results.BadRequest(new
                {
                    message = "DefinitionId é obrigatório."
                });
            }

            var client = await workflowRuntime.CreateClientAsync();

            var result = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(request.DefinitionId),
                CorrelationId = request.CorrelationId
            });

            return Results.Ok(new
            {
                message = "Workflow iniciado pelo runtime.",
                request.DefinitionId,
                request.CorrelationId,
                result
            });
        });

        app.MapPost("/api/generic-workflow/signal", async (
            GenericBookmarkSignalRequest request,
            IWorkflowResumer workflowResumer,
            IWebHostEnvironment environment) =>
        {
            if (string.IsNullOrWhiteSpace(request.BookmarkName))
            {
                return Results.BadRequest(new
                {
                    message = "BookmarkName é obrigatório."
                });
            }

            var bookmarkId = await FindBookmarkIdByNameAsync(
                environment.ContentRootPath,
                request.BookmarkName);

            if (string.IsNullOrWhiteSpace(bookmarkId))
            {
                return Results.NotFound(new
                {
                    request.BookmarkName,
                    message = "Nenhum bookmark encontrado."
                });
            }

            var result = await workflowResumer.ResumeAsync(bookmarkId, request.Input);

            var message = result.Bookmarks?.Any() == true
                ? "Workflow ainda está aguardando outro sinal."
                : "Workflow finalizado com sucesso.";

            return Results.Ok(new
            {
                request.BookmarkName,
                bookmarkId,
                input = request.Input,
                result,
                message
            });
        });

        return app;
    }

    public sealed class GenericWorkflowStartRequest
    {
        public string DefinitionId { get; set; } = "";
        public string CorrelationId { get; set; } = "";
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
            throw new InvalidOperationException("Não foi encontrada coluna de ID na tabela Bookmarks.");

        var payloadColumn = PickFirstExistingColumn(
            columns,
            "Payload",
            "SerializedPayload",
            "SerializedOptions");

        var orderColumn = PickFirstExistingColumn(
            columns,
            "CreatedAt",
            "UpdatedAt",
            "Id") ?? idColumn;

        var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(payloadColumn))
        {
            command.CommandText = $"""
                SELECT [{idColumn}]
                FROM Bookmarks
                WHERE [{payloadColumn}] LIKE $bookmarkName
                ORDER BY [{orderColumn}] DESC
                LIMIT 1
            """;

            command.Parameters.AddWithValue("$bookmarkName", $"%{bookmarkName}%");
        }
        else
        {
            command.CommandText = $"""
                SELECT [{idColumn}]
                FROM Bookmarks
                ORDER BY [{orderColumn}] DESC
                LIMIT 1
            """;
        }

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
}

public sealed class GenericWorkflowStartRequest
{
    public string DefinitionId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
}

public sealed class GenericBookmarkSignalRequest
{
    public string BookmarkName { get; set; } = "";
    public Dictionary<string, object> Input { get; set; } = new();
}