using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;

namespace WorkflowManager.Services;

public sealed class WorkflowExecutionService
{
    private readonly IWorkflowRuntime _workflowRuntime;

    public WorkflowExecutionService(IWorkflowRuntime workflowRuntime)
    {
        _workflowRuntime = workflowRuntime;
    }

    public async Task<IniciarWorkflowResult> IniciarWorkflowAsync(
        string definitionId,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("DefinitionId é obrigatório.", nameof(definitionId));

        var client = await _workflowRuntime.CreateClientAsync();

        var result = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId
        });

        return new IniciarWorkflowResult
        {
            DefinitionId = definitionId,
            CorrelationId = correlationId,
            Result = result
        };
    }
}

public sealed class IniciarWorkflowResult
{
    public string DefinitionId { get; set; } = "";
    public string? CorrelationId { get; set; }
    public object? Result { get; set; }
}