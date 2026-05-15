namespace WorkflowManager.Endpoints;

public static class DesignerObjectEndpoints
{
    public static IEndpointRouteBuilder MapDesignerObjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/designer/objects", async () =>
        {
            // Por enquanto, exemplo mockado.
            // Depois você troca isso por busca real no banco, API ou BionicCrow.
            var objects = new List<DesignerObjectDto>
            {
                new()
                {
                    ObjectId = "objeto-001",
                    Name = "Motor",
                    Description = "Motor principal"
                },
                new()
                {
                    ObjectId = "objeto-002",
                    Name = "Sensor",
                    Description = "Sensor de temperatura"
                },
                new()
                {
                    ObjectId = "objeto-003",
                    Name = "Controlador",
                    Description = "Controlador eletrônico"
                }
            };

            return Results.Ok(objects);
        });

        return app;
    }
}

public sealed class DesignerObjectDto
{
    public string ObjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}