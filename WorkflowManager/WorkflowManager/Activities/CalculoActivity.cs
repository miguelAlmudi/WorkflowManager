using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace WorkflowManager.Activities;

[Activity("BionicCrow")]
public class CalculoActivity : CodeActivity
{
    [Input(Description = "Primeiro valor")]
    public Input<int> A { get; set; } = default!;

    [Input(Description = "Segundo valor")]
    public Input<int> B { get; set; } = default!;

    [Input(Description = "Operação: somar ou multiplicar")]
    public Input<string> Operacao { get; set; } = default!;

    [Output(Description = "Resultado do cálculo")]
    public Output<int> Resultado { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context)
    {
        var a = A.Get(context);
        var b = B.Get(context);
        var operacao = (Operacao.Get(context) ?? "").Trim().ToLowerInvariant();

        var resultado = operacao switch
        {
            "somar" => a + b,
            "soma" => a + b,
            "multiplicar" => a * b,
            "multiplicacao" => a * b,
            "multiplicação" => a * b,
            _ => throw new InvalidOperationException(
                $"Operação inválida: '{operacao}'. Use 'somar' ou 'multiplicar'.")
        };

        Resultado.Set(context, resultado);
    }
}