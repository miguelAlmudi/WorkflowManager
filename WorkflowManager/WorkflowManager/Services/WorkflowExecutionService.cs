using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;

namespace WorkflowManager.Services;

public sealed class WorkflowExecutionService
{
    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IActivitySerializer _activitySerializer;
    private readonly IWorkflowDefinitionImporter _workflowDefinitionImporter;

    public WorkflowExecutionService(
        IWorkflowRuntime workflowRuntime,
        IActivitySerializer activitySerializer,
        IWorkflowDefinitionImporter workflowDefinitionImporter)
    {
        _workflowRuntime = workflowRuntime;
        _activitySerializer = activitySerializer;
        _workflowDefinitionImporter = workflowDefinitionImporter;
    }

    public async Task<IniciarWorkflowResult> IniciarWorkflowAsync(
        string definitionId,
        string? correlationId = null,
        Dictionary<string, object>? input = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("DefinitionId é obrigatório.", nameof(definitionId));

        var finalCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var client = await _workflowRuntime.CreateClientAsync();

        var result = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId),
            CorrelationId = finalCorrelationId,
            Input = input ?? new Dictionary<string, object>()
        });

        return new IniciarWorkflowResult
        {
            DefinitionId = definitionId,
            CorrelationId = finalCorrelationId,
            Result = result
        };
    }

    public async Task<IniciarWorkflowResult> RegistrarPublicarEIniciarWorkflowAsync(
        string workflowJson,
        string? definitionId = null,
        string? correlationId = null,
        Dictionary<string, object>? input = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowJson))
            throw new ArgumentException("WorkflowJson é obrigatório.", nameof(workflowJson));

        var model = _activitySerializer.Deserialize<WorkflowDefinitionModel>(workflowJson);

        if (model is null)
            throw new InvalidOperationException("Não foi possível desserializar o JSON do workflow.");

        if (model.Root is null)
            throw new InvalidOperationException("O workflow não possui Root activity.");

        if (!string.IsNullOrWhiteSpace(definitionId))
            model.DefinitionId = definitionId;

        if (string.IsNullOrWhiteSpace(model.DefinitionId))
            model.DefinitionId = $"generic-{Guid.NewGuid():N}";

        if (string.IsNullOrWhiteSpace(model.Name))
            model.Name = model.DefinitionId;

        model.IsPublished = true;
        model.IsLatest = true;

        var importResult = await _workflowDefinitionImporter.ImportAsync(
            new SaveWorkflowDefinitionRequest
            {
                Model = model,
                Publish = true
            },
            cancellationToken);

        if (!importResult.Succeeded)
        {
            var errors = importResult.ValidationErrors is null
                ? "Erro desconhecido ao publicar workflow."
                : string.Join("; ", importResult.ValidationErrors.Select(x => x.ToString()));

            throw new InvalidOperationException($"Falha ao publicar workflow: {errors}");
        }

        return await IniciarWorkflowAsync(
            definitionId: model.DefinitionId,
            correlationId: correlationId,
            input: input,
            cancellationToken: cancellationToken);
    }
}

public sealed class IniciarWorkflowResult
{
    public string DefinitionId { get; set; } = "";
    public string? CorrelationId { get; set; }
    public object? Result { get; set; }
}