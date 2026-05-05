using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace WorkflowManager.Activities;

[Activity("BionicCrow", "Bookmarks", "Aguarda alteração de Nome e Descrição de um objeto")]
public class WaitForObjectFieldsActivity : Activity
{
    private const string BookmarkName = "ObjectFieldChanged";
    private const string StateVariableName = "ObjectFieldSignals";

    [Input(Description = "Identificador do objeto monitorado")]
    public Input<string> ObjectId { get; set; } = new("objeto-teste");

    [Output(Description = "Nome final recebido")]
    public Output<string> FinalObjectName { get; set; } = default!;

    [Output(Description = "Descrição final recebida")]
    public Output<string> FinalObjectDescription { get; set; } = default!;

    [Output(Description = "Quantidade de campos distintos recebidos")]
    public Output<int> SignalCount { get; set; } = default!;

    [Output(Description = "Sinais recebidos em JSON")]
    public Output<string> SignalsJson { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        await ProcessOrWaitAsync(context);
    }

    private async ValueTask OnResumeAsync(ActivityExecutionContext context)
    {
        await ProcessOrWaitAsync(context);
    }

    private async ValueTask ProcessOrWaitAsync(ActivityExecutionContext context)
    {
        var objectId = context.Get(ObjectId) ?? "objeto-001";

        var signals = SignalStore.GetOrAdd(objectId, _ => new List<ObjectFieldSignal>());

        lock (signals)
        {
            AddSignalFromInput(context, objectId, signals);
        }

        var hasName = signals.Any(x => x.ObjectId == objectId && x.Field == "name");
        var hasDescription = signals.Any(x => x.ObjectId == objectId && x.Field == "description");

        Console.WriteLine($"[WaitForObjectFieldsActivity] ObjectId: {objectId}");
        Console.WriteLine($"[WaitForObjectFieldsActivity] HasName: {hasName}");
        Console.WriteLine($"[WaitForObjectFieldsActivity] HasDescription: {hasDescription}");
        Console.WriteLine($"[WaitForObjectFieldsActivity] Signals: {JsonSerializer.Serialize(signals)}");

        if (hasName && hasDescription)
        {
            var finalName = signals
                .Where(x => x.ObjectId == objectId && x.Field == "name")
                .Last()
                .Value;

            var finalDescription = signals
                .Where(x => x.ObjectId == objectId && x.Field == "description")
                .Last()
                .Value;

            FinalObjectName.Set(context, finalName);
            FinalObjectDescription.Set(context, finalDescription);

            SignalCount.Set(context, 2);

            SignalsJson.Set(context, JsonSerializer.Serialize(
                signals,
                new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine("=================================");
            Console.WriteLine("[Workflow finalizado]");
            Console.WriteLine($"Objeto: {objectId}");
            Console.WriteLine($"Nome: {finalName}");
            Console.WriteLine($"Descrição: {finalDescription}");
            Console.WriteLine("=================================");

            SignalStore.TryRemove(objectId, out _);

            await context.CompleteActivityAsync();
            return;
        }

        var bookmarkName = BuildBookmarkName(objectId);

        Console.WriteLine($"[WaitForObjectFieldsActivity] Criando bookmark: {bookmarkName}");

        context.CreateBookmark(bookmarkName, OnResumeAsync);
    }

    private static string BuildBookmarkName(string objectId)
    {
        return $"{BookmarkName}:{objectId}";
    }

    private static void AddSignalFromInput(
        ActivityExecutionContext context,
        string expectedObjectId,
        List<ObjectFieldSignal> signals)
    {
        var inputObjectId =
            ReadInput(context, "ObjectId") ??
            ReadInput(context, "objectId") ??
            expectedObjectId;

        if (!string.Equals(inputObjectId, expectedObjectId, StringComparison.OrdinalIgnoreCase))
            return;

        var field =
            NormalizeField(ReadInput(context, "Field")) ??
            NormalizeField(ReadInput(context, "field")) ??
            NormalizeField(ReadInput(context, "ChangedField")) ??
            NormalizeField(ReadInput(context, "changedField")) ??
            NormalizeField(ReadInput(context, "Campo"));

        var value =
            ReadInput(context, "Value") ??
            ReadInput(context, "value") ??
            ReadInput(context, "Valor");

        if (field is not null)
        {
            UpsertSignal(signals, expectedObjectId, field, value ?? "");
            return;
        }

        var name =
            ReadInput(context, "Name") ??
            ReadInput(context, "Nome");

        if (name is not null)
            UpsertSignal(signals, expectedObjectId, "name", name);

        var description =
            ReadInput(context, "Description") ??
            ReadInput(context, "Descricao") ??
            ReadInput(context, "Descrição");

        if (description is not null)
            UpsertSignal(signals, expectedObjectId, "description", description);
    }

    private static void UpsertSignal(
        List<ObjectFieldSignal> signals,
        string objectId,
        string field,
        string value)
    {
        signals.RemoveAll(x =>
            string.Equals(x.ObjectId, objectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase));

        signals.Add(new ObjectFieldSignal
        {
            ObjectId = objectId,
            Field = field,
            Value = value,
            ReceivedAt = DateTimeOffset.UtcNow
        });
    }

    private static readonly ConcurrentDictionary<string, List<ObjectFieldSignal>> SignalStore = new();

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

    private static string? ReadInput(ActivityExecutionContext context, string key)
    {
        if (!context.WorkflowInput.TryGetValue(key, out var value))
            return null;

        if (value is null)
            return null;

        return value switch
        {
            string text => text,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
            JsonElement json => json.ToString(),
            _ => value.ToString()
        };
    }


    public static void ClearSignals(string objectId)
    {
        SignalStore.TryRemove(objectId, out _);
    }

    public sealed class ObjectFieldBookmarkPayload
    {
        public string ObjectId { get; set; } = "";
    }

    public sealed class ObjectFieldSignal
    {
        public string ObjectId { get; set; } = "";
        public string Field { get; set; } = "";
        public string Value { get; set; } = "";
        public DateTimeOffset ReceivedAt { get; set; }
    }
}