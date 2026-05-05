using Elsa.Workflows;
using Elsa.Workflows.Models;
using WorkflowManager.Activities;

namespace WorkflowManager.Workflows;

public class ObjectFieldsWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId("ObjectFieldsWorkflow");

        builder.Root = new WaitForObjectFieldsActivity
        {
            ObjectId = new Input<string>("objeto-001")
        };
    }
}