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
                    result = resumeResult
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
                result = startResult.Result
            });
        });


        app.MapGet("/debug-bookmarks", async (IWebHostEnvironment environment) =>
        {
            var dbPath = Path.Combine(environment.ContentRootPath, "elsa.db");
            var connectionString = $"Data Source={dbPath}";

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var columnsCommand = connection.CreateCommand();
            columnsCommand.CommandText = "PRAGMA table_info([Bookmarks])";

            var columns = new List<string>();

            await using (var reader = await columnsCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    columns.Add(reader["name"]?.ToString() ?? "");
            }

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM [Bookmarks] ORDER BY [CreatedAt] DESC LIMIT 20";

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