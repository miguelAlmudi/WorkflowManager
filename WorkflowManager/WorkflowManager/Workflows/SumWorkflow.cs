using Elsa.Workflows;
using Elsa.Workflows.Models;
using WorkflowManager.Activities;

namespace WorkflowManager.Workflows;

public class SumWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId("SumWorkflow");

        builder.Root = new WaitForSumValuesActivity
        {
            CalculationId = new Input<string>("soma-001")
        };
    }
}