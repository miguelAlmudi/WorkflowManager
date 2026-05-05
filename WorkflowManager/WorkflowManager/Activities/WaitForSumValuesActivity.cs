using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace WorkflowManager.Activities;

[Activity("BionicCrow", "Bookmarks", "Aguarda dois valores e realiza uma soma")]
public class WaitForSumValuesActivity : Activity
{
    private const string BookmarkPrefix = "SumValueChanged";

    private static readonly ConcurrentDictionary<string, SumSignalStore> SignalStore = new();

    [Input(Description = "Identificador do cálculo")]
    public Input<string> CalculationId { get; set; } = new("soma-001");

    [Output(Description = "Valor A recebido")]
    public Output<decimal> FinalValueA { get; set; } = default!;

    [Output(Description = "Valor B recebido")]
    public Output<decimal> FinalValueB { get; set; } = default!;

    [Output(Description = "Resultado da soma")]
    public Output<decimal> CalculatedSum { get; set; } = default!;

    [Output(Description = "Sinais recebidos em JSON")]
    public Output<string> SignalsJson { get; set; } = default!;

    public static void ClearSignals(string calculationId)
    {
        SignalStore.TryRemove(calculationId, out _);
    }

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
        var calculationId = context.Get(CalculationId) ?? "soma-001";

        var store = SignalStore.GetOrAdd(calculationId, _ => new SumSignalStore());

        lock (store)
        {
            AddSignalFromInput(context, calculationId, store);
        }

        var hasA = store.HasA;
        var hasB = store.HasB;

        Console.WriteLine("=================================");
        Console.WriteLine("[WaitForSumValuesActivity]");
        Console.WriteLine($"CalculationId: {calculationId}");
        Console.WriteLine($"HasA: {hasA}");
        Console.WriteLine($"HasB: {hasB}");
        Console.WriteLine($"A: {store.ValueA}");
        Console.WriteLine($"B: {store.ValueB}");
        Console.WriteLine("=================================");

        if (hasA && hasB)
        {
            var result = store.ValueA + store.ValueB;

            FinalValueA.Set(context, store.ValueA);
            FinalValueB.Set(context, store.ValueB);
            CalculatedSum.Set(context, result);

            SignalsJson.Set(context, JsonSerializer.Serialize(
                store,
                new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine("=================================");
            Console.WriteLine("[Workflow de soma finalizado]");
            Console.WriteLine($"CalculationId: {calculationId}");
            Console.WriteLine($"A: {store.ValueA}");
            Console.WriteLine($"B: {store.ValueB}");
            Console.WriteLine($"Resultado: {result}");
            Console.WriteLine("=================================");

            SignalStore.TryRemove(calculationId, out _);

            await context.CompleteActivityAsync();
            return;
        }

        var bookmarkName = BuildBookmarkName(calculationId);

        Console.WriteLine($"[WaitForSumValuesActivity] Criando bookmark: {bookmarkName}");

        context.CreateBookmark(bookmarkName, OnResumeAsync);
    }

    private static void AddSignalFromInput(
        ActivityExecutionContext context,
        string expectedCalculationId,
        SumSignalStore store)
    {
        var inputCalculationId =
            ReadInput(context, "CalculationId") ??
            ReadInput(context, "calculationId") ??
            expectedCalculationId;

        if (!string.Equals(inputCalculationId, expectedCalculationId, StringComparison.OrdinalIgnoreCase))
            return;

        var field =
            NormalizeField(ReadInput(context, "Field")) ??
            NormalizeField(ReadInput(context, "field"));

        var valueText =
            ReadInput(context, "Value") ??
            ReadInput(context, "value");

        if (field is null || string.IsNullOrWhiteSpace(valueText))
            return;

        if (!TryParseDecimal(valueText, out var number))
        {
            Console.WriteLine($"[WaitForSumValuesActivity] Valor inválido: {valueText}");
            return;
        }

        if (field == "a")
        {
            store.HasA = true;
            store.ValueA = number;
            store.ReceivedAAt = DateTimeOffset.UtcNow;
        }

        if (field == "b")
        {
            store.HasB = true;
            store.ValueB = number;
            store.ReceivedBAt = DateTimeOffset.UtcNow;
        }
    }

    private static string BuildBookmarkName(string calculationId)
    {
        return $"{BookmarkPrefix}:{calculationId}";
    }

    private static string? NormalizeField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        field = field.Trim().ToLowerInvariant();

        return field switch
        {
            "a" or "valora" or "valor_a" or "valuea" or "value_a" => "a",
            "b" or "valorb" or "valor_b" or "valueb" or "value_b" => "b",
            _ => null
        };
    }

    private static bool TryParseDecimal(string value, out decimal number)
    {
        value = value.Trim().Replace(",", ".");

        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out number);
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
}

public sealed class SumSignalStore
{
    public bool HasA { get; set; }
    public bool HasB { get; set; }

    public decimal ValueA { get; set; }
    public decimal ValueB { get; set; }

    public DateTimeOffset? ReceivedAAt { get; set; }
    public DateTimeOffset? ReceivedBAt { get; set; }
}