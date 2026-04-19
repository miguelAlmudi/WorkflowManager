
using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace WorkflowManager.Activities;

[Activity("BionicCrow", "Foundation", "Invoca um método público de uma classe carregada dinamicamente")]
public class InvokeFoundationMethodActivity : CodeActivity
{
    [Input(Description = "Nome completo do tipo. Ex: BionicCrow.Foundation.Core.TestClass2")]
    public Input<string> TypeName { get; set; } = default!;

    [Input(Description = "Nome do método público")]
    public Input<string> MethodName { get; set; } = default!;

    [Input(Description = "Argumentos do construtor em JSON. Ex: [\"Miguel\",1]")]
    public Input<string?> ConstructorArgsJson { get; set; } = default!;

    [Input(Description = "Argumentos do método em JSON. Ex: [\"Joao\",2]")]
    public Input<string?> MethodArgsJson { get; set; } = default!;

    [Output(Description = "Resultado em texto/JSON")]
    public Output<string> Result { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context)
    {
        var typeName = TypeName.Get(context);
        var methodName = MethodName.Get(context);
        var ctorJson = ConstructorArgsJson.Get(context);
        var methodJson = MethodArgsJson.Get(context);

        var ctorArgs = ParseJsonArray(ctorJson);
        var methodArgs = ParseJsonArray(methodJson);

        var result = ReflectionInvoker.InvokeMethod(
            fullTypeName: typeName,
            methodName: methodName,
            constructorArgs: ctorArgs,
            methodArgs: methodArgs);

        var text = result switch
        {
            null => "",
            string s => s,
            _ => JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
        };

        Result.Set(context, text);
    }

    private static object?[] ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<object?>();

        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("O valor informado deve ser um JSON array.");

        var list = new List<object?>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number when item.TryGetInt32(out var i) => i,
                JsonValueKind.Number when item.TryGetInt64(out var l) => l,
                JsonValueKind.Number when item.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => item.ToString()
            });
        }

        return list.ToArray();
    }
}