using Elsa.Workflows;
using Elsa.Workflows.Models;
using WorkflowManager.Activities;

namespace WorkflowManager.Workflows;

public abstract class ObjectFieldsWorkflowBase : WorkflowBase
{
    protected abstract string WorkflowDefinitionId { get; }
    protected abstract string MonitoredObjectId { get; }

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(WorkflowDefinitionId);

        builder.Root = new WaitForObjectFieldsActivity
        {
            ObjectId = new Input<string>(MonitoredObjectId)
        };
    }
}

public class ObjectFieldsWorkflowMotor : ObjectFieldsWorkflowBase
{
    protected override string WorkflowDefinitionId => "ObjectFieldsWorkflowMotor";
    protected override string MonitoredObjectId => "objeto-001";
}

public class ObjectFieldsWorkflowSensor : ObjectFieldsWorkflowBase
{
    protected override string WorkflowDefinitionId => "ObjectFieldsWorkflowSensor";
    protected override string MonitoredObjectId => "objeto-002";
}

public class ObjectFieldsWorkflowControlador : ObjectFieldsWorkflowBase
{
    protected override string WorkflowDefinitionId => "ObjectFieldsWorkflowControlador";
    protected override string MonitoredObjectId => "objeto-003";
}