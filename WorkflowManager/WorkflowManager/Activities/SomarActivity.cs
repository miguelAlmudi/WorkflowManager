using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace WorkflowManager.Activities;

[Activity("BionicCrow")]
public class SomarActivity : CodeActivity
{
    [Input(Description = "Primeiro valor")]
    public Input<int> A { get; set; } = default!;

    [Input(Description = "Segundo valor")]
    public Input<int> B { get; set; } = default!;

    [Output(Description = "Resultado da soma")]
    public Output<int> Resultado { get; set; } = default!;

    protected override void Execute(ActivityExecutionContext context)
    {
        var a = A.Get(context);
        var b = B.Get(context);

        Resultado.Set(context, a + b);
    }
}
