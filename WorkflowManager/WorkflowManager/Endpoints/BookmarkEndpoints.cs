using Elsa.Workflows.Runtime;

namespace WorkflowManager.Endpoints;

public static class BookmarkEndpoints
{
    public static IEndpointRouteBuilder MapBookmarkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bookmarks/resume-by-id", async (
            ResumeBookmarkByIdRequest request,
            IWorkflowResumer workflowResumer) =>
        {
            var input = new Dictionary<string, object>
            {
                ["Message"] = request.Message ?? ""
            };

            var result = await workflowResumer.ResumeAsync(request.BookmarkId, input);

            return Results.Ok(new
            {
                request.BookmarkId,
                request.Message,
                resumed = result != null
            });
        });

        return app;
    }
}

public sealed class ResumeBookmarkByIdRequest
{
    public string BookmarkId { get; set; } = "";
    public string? Message { get; set; }
}