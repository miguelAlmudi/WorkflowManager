using Elsa.Workflows.Runtime;
using Microsoft.Data.Sqlite;
using WorkflowManager.Activities;

namespace WorkflowManager.Endpoints;

public static class SumWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapSumWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sum-workflow/signal", async (
            SumWorkflowSignalRequest request,
            IWorkflowResumer workflowResumer,
            IWebHostEnvironment environment) =>
        {
            var calculationId = string.IsNullOrWhiteSpace(request.CalculationId)
                ? "soma-001"
                : request.CalculationId;

            var normalizedField = NormalizeField(request.Field);

            if (normalizedField is null)
            {
                return Results.BadRequest(new
                {
                    message = "Campo inválido. Use 'a' ou 'b'."
                });
            }

            var bookmarkName = $"SumValueChanged:{calculationId}";

            var bookmarkId = await FindBookmarkIdByNameAsync(
                environment.ContentRootPath,
                bookmarkName);

            if (string.IsNullOrWhiteSpace(bookmarkId))
            {
                return Results.NotFound(new
                {
                    calculationId,
                    bookmarkName,
                    message = "Nenhum bookmark de soma encontrado."
                });
            }

            var input = new Dictionary<string, object>
            {
                ["CalculationId"] = calculationId,
                ["Field"] = normalizedField,
                ["Value"] = request.Value ?? ""
            };

            var result = await workflowResumer.ResumeAsync(bookmarkId, input);

            var message = result.Bookmarks?.Any() == true
                ? "Workflow de soma ainda está aguardando outro valor."
                : "Workflow de soma finalizado com sucesso.";

            return Results.Ok(new
            {
                calculationId,
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

    private static string? NormalizeField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        field = field.Trim().ToLowerInvariant();

        return field switch
        {
            "a" or "valora" or "valor_a" => "a",
            "b" or "valorb" or "valor_b" => "b",
            _ => null
        };
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

public sealed class SumWorkflowSignalRequest
{
    public string CalculationId { get; set; } = "soma-001";
    public string Field { get; set; } = "";
    public string? Value { get; set; }
}