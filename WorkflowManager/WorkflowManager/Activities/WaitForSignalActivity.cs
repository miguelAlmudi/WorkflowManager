using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using System.Text.Json;

namespace WorkflowManager.Activities;

[Activity("BionicCrow", "Bookmarks", "Pausa o workflow até receber um sinal externo")]
public class WaitForSignalActivity : Activity
{
    [Input(Description = "Nome do sinal que deve retomar o workflow")]
    public Input<string> SignalName { get; set; } = default!;

    [Output(Description = "Mensagem recebida ao retomar")]
    public Output<string> Message { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var signalName = context.Get(SignalName);

        context.CreateBookmark(signalName, OnResumeAsync);

        await ValueTask.CompletedTask;
    }

    private async ValueTask OnResumeAsync(ActivityExecutionContext context)
    {
        var message = "";

        if (context.WorkflowInput.TryGetValue("Message", out var value))
        {
            message = value switch
            {
                null => "",
                string s => s,
                JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString() ?? "",
                JsonElement json => json.ToString(),
                _ => value.ToString() ?? ""
            };
        }

        Message.Set(context, message);

        await context.CompleteActivityAsync();
    }
}