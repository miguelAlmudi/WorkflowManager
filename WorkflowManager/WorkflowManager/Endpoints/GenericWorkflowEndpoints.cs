using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Data.Sqlite;
using WorkflowManager.Services;
using System.Text.Json;

namespace WorkflowManager.Endpoints;

public static class GenericWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapGenericWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/generic-workflow/start", async (
    GenericWorkflowStartRequest request,
    WorkflowExecutionService workflowExecutionService,
    CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DefinitionId) &&
                string.IsNullOrWhiteSpace(request.WorkflowJson))
            {
                return Results.BadRequest(new
                {
                    message = "Informe DefinitionId ou WorkflowJson."
                });
            }

            IniciarWorkflowResult result;

            if (!string.IsNullOrWhiteSpace(request.WorkflowJson))
            {
                result = await workflowExecutionService.RegistrarPublicarEIniciarWorkflowAsync(
                    workflowJson: request.WorkflowJson,
                    definitionId: request.DefinitionId,
                    correlationId: request.CorrelationId,
                    input: request.Input,
                    cancellationToken: cancellationToken);
            }
            else
            {
                result = await workflowExecutionService.IniciarWorkflowAsync(
                    definitionId: request.DefinitionId,
                    correlationId: request.CorrelationId,
                    input: request.Input,
                    cancellationToken: cancellationToken);
            }

            return Results.Ok(new
            {
                message = string.IsNullOrWhiteSpace(request.WorkflowJson)
                    ? "Workflow iniciado pelo runtime."
                    : "Workflow registrado/publicado dinamicamente e iniciado pelo runtime.",
                result.DefinitionId,
                result.CorrelationId,
                result = SafeWorkflowResult.From(result.Result)
            });
        });


        app.MapPost("/api/generic-workflow/register-json-file", async (
            GenericWorkflowJsonRegisterRequest request,
            IWebHostEnvironment environment) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
            {
                return Results.BadRequest(new
                {
                    message = "JSON do workflow é obrigatório."
                });
            }

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(request.Json);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new
                {
                    message = "JSON inválido.",
                    error = ex.Message
                });
            }

            using (document)
            {
                var root = document.RootElement;

                var definitionId = root.TryGetProperty("definitionId", out var definitionIdElement)
                    ? definitionIdElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(definitionId))
                {
                    return Results.BadRequest(new
                    {
                        message = "O workflow JSON precisa conter a propriedade 'definitionId'."
                    });
                }

                var workflowsFolder = Path.Combine(environment.ContentRootPath, "Workflows");

                Directory.CreateDirectory(workflowsFolder);

                var safeFileName = MakeSafeFileName(definitionId);
                var filePath = Path.Combine(workflowsFolder, $"{safeFileName}.json");

                var formattedJson = JsonSerializer.Serialize(
                    root,
                    new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(filePath, formattedJson);

                return Results.Ok(new
                {
                    message = "Workflow JSON salvo na pasta Workflows.",
                    definitionId,
                    filePath,
                    warning = "Salvar o arquivo não garante que o Elsa Runtime já reconheça o workflow sem um provider/publicação dinâmica configurado."
                });
            }
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


    private static string MakeSafeFileName(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return value;
    }

    public sealed class GenericWorkflowJsonRegisterRequest
    {
        public string Json { get; set; } = "";
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
    public string? WorkflowJson { get; set; }
    public Dictionary<string, object>? Input { get; set; }
}

public sealed class GenericBookmarkSignalRequest
{
    public string BookmarkName { get; set; } = "";
    public Dictionary<string, object> Input { get; set; } = new();
}

public sealed class SafeWorkflowResult
{
    public string? WorkflowInstanceId { get; set; }
    public string? Status { get; set; }
    public string? SubStatus { get; set; }
    public List<SafeBookmark> Bookmarks { get; set; } = new();
    public List<SafeIncident> Incidents { get; set; } = new();

    public static SafeWorkflowResult? From(object? result)
    {
        if (result is null)
            return null;

        return new SafeWorkflowResult
        {
            WorkflowInstanceId = ToSafeText(GetProperty(result, "WorkflowInstanceId")),
            Status = ToSafeText(GetProperty(result, "Status")),
            SubStatus = ToSafeText(GetProperty(result, "SubStatus")),
            Bookmarks = MapBookmarks(GetProperty(result, "Bookmarks")),
            Incidents = MapIncidents(GetProperty(result, "Incidents"))
        };
    }

    private static List<SafeBookmark> MapBookmarks(object? bookmarksObject)
    {
        var list = new List<SafeBookmark>();

        if (bookmarksObject is not System.Collections.IEnumerable enumerable)
            return list;

        foreach (var bookmark in enumerable)
        {
            list.Add(new SafeBookmark
            {
                Id = ToSafeText(GetProperty(bookmark, "Id")),
                Name = ToSafeText(GetProperty(bookmark, "Name")),
                Payload = ToSafeText(GetProperty(bookmark, "Payload")),
                ActivityId = ToSafeText(GetProperty(bookmark, "ActivityId")),
                ActivityNodeId = ToSafeText(GetProperty(bookmark, "ActivityNodeId")),
                ActivityInstanceId = ToSafeText(GetProperty(bookmark, "ActivityInstanceId")),
                CreatedAt = ToSafeText(GetProperty(bookmark, "CreatedAt"))
            });
        }

        return list;
    }

    private static List<SafeIncident> MapIncidents(object? incidentsObject)
    {
        var list = new List<SafeIncident>();

        if (incidentsObject is not System.Collections.IEnumerable enumerable)
            return list;

        foreach (var incident in enumerable)
        {
            var exceptionObject = GetProperty(incident, "Exception");

            list.Add(new SafeIncident
            {
                ActivityId = ToSafeText(GetProperty(incident, "ActivityId")),
                ActivityNodeId = ToSafeText(GetProperty(incident, "ActivityNodeId")),
                Message =
                    ToSafeText(GetProperty(incident, "Message")) ??
                    ReadExceptionMessage(exceptionObject),
                ExceptionType = ReadExceptionType(exceptionObject),
                ExceptionMessage = ReadExceptionMessage(exceptionObject),
                StackTrace = ReadExceptionStackTrace(exceptionObject)
            });
        }

        return list;
    }

    private static object? GetProperty(object? obj, string propertyName)
    {
        if (obj is null)
            return null;

        var property = obj
            .GetType()
            .GetProperties()
            .FirstOrDefault(x => x.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

        return property?.GetValue(obj);
    }

    private static string? ToSafeText(object? value)
    {
        if (value is null)
            return null;

        return value switch
        {
            Type type => type.FullName,
            Exception exception => $"{exception.GetType().FullName}: {exception.Message}",
            _ => value.ToString()
        };
    }

    private static string? ReadExceptionType(object? exceptionObject)
    {
        if (exceptionObject is null)
            return null;

        if (exceptionObject is Exception exception)
            return exception.GetType().FullName;

        var typeValue = GetProperty(exceptionObject, "Type");

        return typeValue switch
        {
            Type type => type.FullName,
            null => exceptionObject.GetType().FullName,
            _ => typeValue.ToString()
        };
    }

    private static string? ReadExceptionMessage(object? exceptionObject)
    {
        if (exceptionObject is null)
            return null;

        if (exceptionObject is Exception exception)
            return exception.Message;

        return ToSafeText(GetProperty(exceptionObject, "Message"));
    }

    private static string? ReadExceptionStackTrace(object? exceptionObject)
    {
        if (exceptionObject is null)
            return null;

        if (exceptionObject is Exception exception)
            return exception.StackTrace;

        return ToSafeText(GetProperty(exceptionObject, "StackTrace"));
    }
}

public sealed class SafeBookmark
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Payload { get; set; }
    public string? ActivityId { get; set; }
    public string? ActivityNodeId { get; set; }
    public string? ActivityInstanceId { get; set; }
    public string? CreatedAt { get; set; }
}

public sealed class SafeIncident
{
    public string? ActivityId { get; set; }
    public string? ActivityNodeId { get; set; }
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
}