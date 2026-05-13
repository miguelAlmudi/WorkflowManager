using Elsa.Workflows.Runtime;
using Microsoft.Data.Sqlite;
using WorkflowManager.Services;

namespace WorkflowManager.Endpoints;

public static class TriggerEndpoints
{
    public static IEndpointRouteBuilder MapTriggerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/triggers/object-field-changed", async (
            ObjectFieldChangedTriggerRequest request,
            WorkflowExecutionService workflowExecutionService,
            IWorkflowResumer workflowResumer,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ObjectId))
            {
                return Results.BadRequest(new
                {
                    message = "ObjectId é obrigatório."
                });
            }

            if (string.IsNullOrWhiteSpace(request.Field))
            {
                return Results.BadRequest(new
                {
                    message = "Field é obrigatório."
                });
            }

            var bookmarkName = $"ObjectFieldChanged:{request.ObjectId}";

            var input = new Dictionary<string, object>
            {
                ["ObjectId"] = request.ObjectId,
                ["Field"] = request.Field,
                ["Value"] = request.Value ?? ""
            };

            var bookmarkId = await FindBookmarkIdByNameAsync(
                environment.ContentRootPath,
                bookmarkName);

            if (!string.IsNullOrWhiteSpace(bookmarkId))
            {
                var resumeResult = await workflowResumer.ResumeAsync(bookmarkId, input);

                return Results.Ok(new
                {
                    mode = "resume",
                    message = "Workflow existente retomado por trigger.",
                    bookmarkName,
                    bookmarkId,
                    input,
                    result = SafeWorkflowResult.From(resumeResult)
                });
            }

            var definitionId = string.IsNullOrWhiteSpace(request.DefinitionId)
                ? "ObjectFieldsWorkflowMotor"
                : request.DefinitionId;

            var startResult = await workflowExecutionService.IniciarWorkflowAsync(
                definitionId: definitionId,
                correlationId: request.ObjectId,
                input: input,
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                mode = "start",
                message = "Nenhum bookmark existente foi encontrado. Um novo workflow foi iniciado pelo trigger.",
                definitionId,
                correlationId = request.ObjectId,
                input,
                result = SafeWorkflowResult.From(startResult.Result)
            });
        });

        app.MapPost("/api/triggers/event", async (
            WorkflowEventTriggerRequest request,
            WorkflowExecutionService workflowExecutionService,
            IWorkflowResumer workflowResumer,
            IWebHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.EventName))
            {
                return Results.BadRequest(new
                {
                    message = "EventName é obrigatório."
                });
            }

            if (string.IsNullOrWhiteSpace(request.CorrelationId))
            {
                return Results.BadRequest(new
                {
                    message = "CorrelationId é obrigatório."
                });
            }

            var bookmarkName = $"{request.EventName}:{request.CorrelationId}";

            var input = new Dictionary<string, object>
            {
                ["EventName"] = request.EventName,
                ["CorrelationId"] = request.CorrelationId,
                ["BookmarkName"] = bookmarkName
            };

            if (request.Data is not null)
            {
                foreach (var item in request.Data)
                    input[item.Key] = item.Value ?? "";
            }

            var bookmarkId = await FindBookmarkIdByNameAsync(
                environment.ContentRootPath,
                bookmarkName);

            if (!string.IsNullOrWhiteSpace(bookmarkId))
            {
                var resumeResult = await workflowResumer.ResumeAsync(bookmarkId, input);

                return Results.Ok(new
                {
                    mode = "resume",
                    message = "Workflow existente retomado pelo trigger.",
                    bookmarkName,
                    bookmarkId,
                    input,
                    result = SafeWorkflowResult.From(resumeResult)
                });
            }

            if (string.IsNullOrWhiteSpace(request.DefinitionId))
            {
                return Results.NotFound(new
                {
                    mode = "not_found",
                    message = "Nenhum bookmark encontrado e nenhum DefinitionId foi informado para iniciar novo workflow.",
                    bookmarkName
                });
            }

            var startResult = await workflowExecutionService.IniciarWorkflowAsync(
                definitionId: request.DefinitionId,
                correlationId: request.CorrelationId,
                input: input,
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                mode = "start",
                message = "Nenhum bookmark encontrado. Novo workflow iniciado pelo trigger.",
                request.DefinitionId,
                request.CorrelationId,
                bookmarkName,
                input,
                result = SafeWorkflowResult.From(startResult.Result)
            });
        });

        app.MapGet("/debug-bookmarks", async (IWebHostEnvironment environment) =>
        {
            var dbPath = Path.Combine(environment.ContentRootPath, "elsa.db");
            var connectionString = $"Data Source={dbPath}";

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var columns = await GetTableColumnsAsync(connection, "Bookmarks");

            var orderColumn = PickFirstExistingColumn(
                columns,
                "CreatedAt",
                "UpdatedAt",
                "Id");

            var command = connection.CreateCommand();

            command.CommandText = string.IsNullOrWhiteSpace(orderColumn)
                ? "SELECT * FROM [Bookmarks] LIMIT 20"
                : $"SELECT * FROM [Bookmarks] ORDER BY [{orderColumn}] DESC LIMIT 20";

            var rows = new List<Dictionary<string, object?>>();

            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();

                    foreach (var column in columns)
                        row[column] = reader[column]?.ToString();

                    rows.Add(row);
                }
            }

            return Results.Ok(new
            {
                database = dbPath,
                columns,
                count = rows.Count,
                bookmarks = rows
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
            throw new InvalidOperationException("Não foi encontrada coluna de ID na tabela Bookmarks.");

        var searchableColumns = columns
            .Where(x =>
                x.Equals("Payload", StringComparison.OrdinalIgnoreCase) ||
                x.Equals("SerializedPayload", StringComparison.OrdinalIgnoreCase) ||
                x.Equals("SerializedOptions", StringComparison.OrdinalIgnoreCase) ||
                x.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                x.Equals("Hash", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var orderColumn = PickFirstExistingColumn(
            columns,
            "CreatedAt",
            "UpdatedAt",
            "Id") ?? idColumn;

        var command = connection.CreateCommand();

        if (searchableColumns.Any())
        {
            var where = string.Join(
                " OR ",
                searchableColumns.Select(c => $"[{c}] LIKE $bookmarkName"));

            command.CommandText = $"""
                SELECT [{idColumn}]
                FROM [Bookmarks]
                WHERE {where}
                ORDER BY [{orderColumn}] DESC
                LIMIT 1
            """;

            command.Parameters.AddWithValue("$bookmarkName", $"%{bookmarkName}%");
        }
        else
        {
            command.CommandText = $"""
                SELECT [{idColumn}]
                FROM [Bookmarks]
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

public sealed class ObjectFieldChangedTriggerRequest
{
    public string DefinitionId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string Field { get; set; } = "";
    public string? Value { get; set; }
}

public sealed class WorkflowEventTriggerRequest
{
    public string DefinitionId { get; set; } = "";
    public string EventName { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public Dictionary<string, object?>? Data { get; set; }
}