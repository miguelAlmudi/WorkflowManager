using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using System.Text.Json;
using Elsa.Workflows;
using Elsa.Workflows.Management.Mappers;
using Elsa.Workflows.Management.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

namespace WorkflowManager.Components.Pages
{
    public partial class WorkflowViewer
    {
        private const long MaxFileSize = 5 * 1024 * 1024;
        private const double NodeWidth = 240;
        private const double NodeHeight = 140;
        private const double HorizontalGap = 130;
        private const double VerticalGap = 40;
        private const double CanvasPadding = 60;

        private string? StatusMessage;
        private bool HasError;
        private string? FormattedJson;
        private WorkflowViewModel? Workflow;

        private List<GraphNode> GraphNodes { get; set; } = new();
        private List<GraphEdge> GraphEdges { get; set; } = new();

        private string? SelectedNodeId;
        private string? _draggingNodeId;
        private double _lastMouseX;
        private double _lastMouseY;

        private double CanvasWidth = 1600;
        private double CanvasHeight = 900;

        [Inject] public IServiceProvider Services { get; set; } = default!;

        private string? RawWorkflowJson;
        private string? ExecutionStateJson;

        private GraphNode? SelectedNode => GraphNodes.FirstOrDefault(x => x.Id == SelectedNodeId);

        private async Task LoadWorkflowFile(InputFileChangeEventArgs e)
        {
            ClearState();

            try
            {
                var file = e.File;

                if (file is null)
                {
                    HasError = true;
                    StatusMessage = "Nenhum arquivo foi selecionado.";
                    return;
                }

                using var stream = file.OpenReadStream(MaxFileSize);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                RawWorkflowJson = json;

                using var document = JsonDocument.Parse(json);

                FormattedJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Workflow = BuildWorkflowViewModel(document.RootElement);
                BuildGraphLayout();

                HasError = false;
                StatusMessage = "Workflow carregado com sucesso.";
            }
            catch (JsonException ex)
            {
                HasError = true;
                StatusMessage = $"O arquivo não contém um JSON válido: {ex.Message}";
            }
            catch (IOException ex)
            {
                HasError = true;
                StatusMessage = $"Erro ao ler o arquivo: {ex.Message}";
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = $"Erro ao processar o workflow: {ex.Message}";
            }
        }

        private async Task ExecuteWorkflowAsync()
        {
            if (string.IsNullOrWhiteSpace(RawWorkflowJson))
            {
                HasError = true;
                StatusMessage = "Carregue um workflow JSON antes de executar.";
                return;
            }

            try
            {
                var serializer = Services.GetRequiredService<IActivitySerializer>();
                var mapper = Services.GetRequiredService<WorkflowDefinitionMapper>();
                var runner = Services.GetRequiredService<IWorkflowRunner>();

                var workflowDefinitionModel = serializer.Deserialize<WorkflowDefinitionModel>(RawWorkflowJson);

                if (workflowDefinitionModel == null)
                    throw new InvalidOperationException("Não foi possível desserializar o workflow.");

                var workflow = mapper.Map(workflowDefinitionModel);
                var result = await runner.RunAsync(workflow);

                ExecutionStateJson = JsonSerializer.Serialize(
                    result.WorkflowState,
                    new JsonSerializerOptions { WriteIndented = true });

                HasError = false;
                StatusMessage = $"Execução concluída. Status: {result.WorkflowState.Status}";
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = $"Erro ao executar o workflow: {ex.Message}";
            }
        }

        private void BuildGraphLayout()
        {
            GraphNodes.Clear();
            GraphEdges.Clear();
            SelectedNodeId = null;

            if (Workflow is null)
                return;

            var rootId = Workflow.RootId ?? "root";
            var rootLabel = Workflow.RootDisplayName ?? Workflow.RootType ?? "Root";
            var rootType = Workflow.RootType ?? "Root";

            GraphNodes.Add(new GraphNode
            {
                Id = rootId,
                Label = rootLabel,
                Type = rootType,
                Depth = 0,
                X = CanvasPadding,
                Y = CanvasPadding,
                Kind = "root",
                Highlights = new List<ActivityHighlight>()
            });

            foreach (var activity in Workflow.Activities)
            {
                GraphNodes.Add(new GraphNode
                {
                    Id = activity.Id,
                    Label = activity.DisplayName,
                    Type = activity.ShortType,
                    Depth = Math.Max(activity.Depth, 1),
                    Kind = ClassifyNodeKind(activity.ShortType),
                    Highlights = activity.Highlights
                });
            }

            var nodeMap = GraphNodes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var connection in Workflow.Connections)
            {
                if (!nodeMap.ContainsKey(connection.SourceId) || !nodeMap.ContainsKey(connection.TargetId))
                    continue;

                GraphEdges.Add(new GraphEdge
                {
                    SourceId = connection.SourceId,
                    TargetId = connection.TargetId,
                    Outcome = connection.Outcome
                });
            }

            AutoArrangeNodes();

            if (GraphNodes.Any())
                SelectedNodeId = GraphNodes[0].Id;
        }

        private void AutoArrangeNodes()
        {
            var levels = GraphNodes
                .GroupBy(x => x.Depth)
                .OrderBy(x => x.Key)
                .ToList();

            foreach (var level in levels)
            {
                var nodes = level.ToList();

                for (var i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    node.X = CanvasPadding + level.Key * (NodeWidth + HorizontalGap);
                    node.Y = CanvasPadding + i * (NodeHeight + VerticalGap);
                }
            }

            RecalculateCanvasSize();
        }

        private void RecalculateCanvasSize()
        {
            if (!GraphNodes.Any())
            {
                CanvasWidth = 1600;
                CanvasHeight = 900;
                return;
            }

            CanvasWidth = Math.Max(1600, GraphNodes.Max(x => x.X) + NodeWidth + CanvasPadding);
            CanvasHeight = Math.Max(900, GraphNodes.Max(x => x.Y) + NodeHeight + CanvasPadding);
        }

        private void ResetLayout()
        {
            AutoArrangeNodes();
        }

        private void SelectNode(GraphNode node)
        {
            SelectedNodeId = node.Id;
        }

        private void StartDrag(GraphNode node, MouseEventArgs e)
        {
            SelectedNodeId = node.Id;
            _draggingNodeId = node.Id;
            _lastMouseX = e.ClientX;
            _lastMouseY = e.ClientY;
        }

        private void OnCanvasMouseMove(MouseEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_draggingNodeId))
                return;

            var node = GraphNodes.FirstOrDefault(x => x.Id == _draggingNodeId);

            if (node is null)
                return;

            var dx = e.ClientX - _lastMouseX;
            var dy = e.ClientY - _lastMouseY;

            node.X = Math.Max(0, node.X + dx);
            node.Y = Math.Max(0, node.Y + dy);

            _lastMouseX = e.ClientX;
            _lastMouseY = e.ClientY;

            RecalculateCanvasSize();
        }

        private void OnCanvasMouseUp(MouseEventArgs _)
        {
            _draggingNodeId = null;
        }

        private string NodeStyle(GraphNode node)
            => $"left:{node.X}px; top:{node.Y}px; width:{NodeWidth}px; min-height:{NodeHeight}px;";

        private GraphNode? FindNode(string nodeId)
            => GraphNodes.FirstOrDefault(x => x.Id.Equals(nodeId, StringComparison.OrdinalIgnoreCase));

        private string GetNodeCssClass(GraphNode node)
            => node.Kind switch
            {
                "root" => "graph-node-root",
                "control" => "graph-node-control",
                "http" => "graph-node-http",
                "blocking" => "graph-node-blocking",
                "data" => "graph-node-data",
                _ => "graph-node-generic"
            };

        private static string ClassifyNodeKind(string? shortType)
        {
            var type = shortType?.ToLowerInvariant() ?? string.Empty;

            if (type.Contains("flowchart") || type.Contains("sequence") || type.Contains("decision") ||
                type.Contains("if") || type.Contains("switch") || type.Contains("while") ||
                type.Contains("for") || type.Contains("fork") || type.Contains("parallel"))
                return "control";

            if (type.Contains("http"))
                return "http";

            if (type.Contains("delay") || type.Contains("timer") || type.Contains("event") || type.Contains("signal"))
                return "blocking";

            if (type.Contains("write") || type.Contains("read") || type.Contains("variable") || type.Contains("set"))
                return "data";

            return "generic";
        }

        private WorkflowViewModel BuildWorkflowViewModel(JsonElement workflowElement)
        {
            var model = new WorkflowViewModel
            {
                WorkflowId = GetString(workflowElement, "id"),
                DefinitionId = GetString(workflowElement, "definitionId"),
                Name = GetString(workflowElement, "name")
            };

            if (workflowElement.TryGetProperty("root", out var rootActivity) && rootActivity.ValueKind == JsonValueKind.Object)
            {
                model.RootId = GetString(rootActivity, "id");
                model.RootType = ShortType(GetString(rootActivity, "type"));
                model.RootDisplayName =
                    GetString(rootActivity, "name") ??
                    GetString(rootActivity, "displayName") ??
                    model.RootType;

                var activities = new List<ActivityNode>();
                var structuralConnections = new List<ConnectionInfo>();
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var order = 0;

                CollectChildActivities(
                    rootActivity,
                    parentId: model.RootId ?? "root",
                    parentLabel: model.RootDisplayName ?? model.RootType ?? "Root",
                    depth: 1,
                    activities: activities,
                    structuralConnections: structuralConnections,
                    seenIds: seenIds,
                    order: ref order);

                model.Activities = activities
                    .OrderBy(x => x.Order)
                    .ToList();

                var explicitConnections = ExtractExplicitConnections(rootActivity, model.Activities);

                model.Connections = explicitConnections.Any()
                    ? explicitConnections
                    : structuralConnections
                        .DistinctBy(x => $"{x.SourceId}|{x.TargetId}|{x.Outcome}")
                        .ToList();
            }

            return model;
        }

        private void CollectChildActivities(
            JsonElement container,
            string parentId,
            string parentLabel,
            int depth,
            List<ActivityNode> activities,
            List<ConnectionInfo> structuralConnections,
            HashSet<string> seenIds,
            ref int order)
        {
            foreach (var childRef in EnumerateChildActivities(container))
            {
                var child = childRef.Element;
                var childId = GetString(child, "id") ?? $"activity-{order + 1}";
                var childType = ShortType(GetString(child, "type"));
                var childDisplay =
                    GetString(child, "name") ??
                    GetString(child, "displayName") ??
                    childType ??
                    childId;

                var isNew = seenIds.Add(childId);

                if (!isNew)
                    continue;

                order++;

                var node = new ActivityNode
                {
                    Id = childId,
                    Type = GetString(child, "type") ?? "Unknown",
                    ShortType = childType ?? "Unknown",
                    DisplayName = childDisplay,
                    Description = GetString(child, "description"),
                    SourceProperty = BeautifyLabel(childRef.SourceProperty),
                    Depth = depth,
                    Order = order,
                    Highlights = ExtractHighlights(child)
                };

                activities.Add(node);

                structuralConnections.Add(new ConnectionInfo
                {
                    SourceId = parentId,
                    SourceLabel = parentLabel,
                    TargetId = node.Id,
                    TargetLabel = node.DisplayName,
                    Outcome = node.SourceProperty
                });

                CollectChildActivities(
                    child,
                    parentId: node.Id,
                    parentLabel: node.DisplayName,
                    depth: depth + 1,
                    activities: activities,
                    structuralConnections: structuralConnections,
                    seenIds: seenIds,
                    order: ref order);
            }
        }

        private List<ConnectionInfo> ExtractExplicitConnections(JsonElement rootActivity, IReadOnlyList<ActivityNode> activities)
        {
            var result = new List<ConnectionInfo>();

            if (!rootActivity.TryGetProperty("connections", out var connections) || connections.ValueKind != JsonValueKind.Array)
                return result;

            var activityMap = activities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var connection in connections.EnumerateArray())
            {
                var sourceId =
                    FirstNonEmpty(
                        GetString(connection, "sourceActivityId"),
                        GetEndpointActivityId(connection, "source"),
                        GetEndpointActivityId(connection, "sourceEndpoint"),
                        GetEndpointActivityId(connection, "from"));

                var targetId =
                    FirstNonEmpty(
                        GetString(connection, "targetActivityId"),
                        GetEndpointActivityId(connection, "target"),
                        GetEndpointActivityId(connection, "targetEndpoint"),
                        GetEndpointActivityId(connection, "to"));

                if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
                    continue;

                var outcome =
                    FirstNonEmpty(
                        GetString(connection, "outcome"),
                        GetString(connection, "port"),
                        GetEndpointLabel(connection, "source"),
                        GetEndpointLabel(connection, "sourceEndpoint"),
                        GetEndpointLabel(connection, "from"));

                result.Add(new ConnectionInfo
                {
                    SourceId = sourceId!,
                    SourceLabel = activityMap.TryGetValue(sourceId!, out var sourceNode) ? sourceNode.DisplayName : sourceId!,
                    TargetId = targetId!,
                    TargetLabel = activityMap.TryGetValue(targetId!, out var targetNode) ? targetNode.DisplayName : targetId!,
                    Outcome = outcome
                });
            }

            return result
                .DistinctBy(x => $"{x.SourceId}|{x.TargetId}|{x.Outcome}")
                .ToList();
        }

        private IEnumerable<ActivityReference> EnumerateChildActivities(JsonElement activityElement)
        {
            if (activityElement.TryGetProperty("activities", out var activitiesArray) && activitiesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in activitiesArray.EnumerateArray())
                {
                    if (LooksLikeActivity(item))
                        yield return new ActivityReference(item, "activities");
                }
            }

            foreach (var property in activityElement.EnumerateObject())
            {
                if (property.NameEquals("activities") || property.NameEquals("connections"))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    if (LooksLikeActivity(property.Value))
                    {
                        yield return new ActivityReference(property.Value, property.Name);
                        continue;
                    }

                    if (TryGetWrappedActivity(property.Value, out var wrappedActivity))
                        yield return new ActivityReference(wrappedActivity, property.Name);
                }

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (LooksLikeActivity(item))
                        {
                            yield return new ActivityReference(item, property.Name);
                            continue;
                        }

                        if (TryGetWrappedActivity(item, out var wrappedActivity))
                            yield return new ActivityReference(wrappedActivity, property.Name);
                    }
                }
            }
        }

        private static bool TryGetWrappedActivity(JsonElement element, out JsonElement activity)
        {
            activity = default;

            if (element.ValueKind != JsonValueKind.Object)
                return false;

            if (element.TryGetProperty("activity", out var activityProperty) && LooksLikeActivity(activityProperty))
            {
                activity = activityProperty;
                return true;
            }

            return false;
        }

        private static bool LooksLikeActivity(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return false;

            if (element.TryGetProperty("expression", out _) &&
                element.TryGetProperty("typeName", out _) &&
                !element.TryGetProperty("id", out _))
                return false;

            if (!element.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
                return false;

            var typeName = typeProperty.GetString();

            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            if (element.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String)
                return true;

            if (element.TryGetProperty("activities", out var activitiesProperty) && activitiesProperty.ValueKind == JsonValueKind.Array)
                return true;

            if (typeName.StartsWith("Elsa.", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private List<ActivityHighlight> ExtractHighlights(JsonElement activity)
        {
            var result = new List<ActivityHighlight>();

            var preferredProperties = new[]
            {
            "text", "path", "url", "condition", "variable", "value",
            "method", "message", "statusCodes", "canStartWorkflow",
            "cronExpression", "delay", "content", "name"
        };

            foreach (var propertyName in preferredProperties)
            {
                if (result.Count >= 5)
                    break;

                if (!activity.TryGetProperty(propertyName, out var propertyValue))
                    continue;

                var preview = ExtractPreview(propertyValue);

                if (string.IsNullOrWhiteSpace(preview))
                    continue;

                result.Add(new ActivityHighlight
                {
                    Key = BeautifyLabel(propertyName),
                    Value = preview
                });
            }

            foreach (var property in activity.EnumerateObject())
            {
                if (result.Count >= 5)
                    break;

                if (IsIgnoredProperty(property.Name))
                    continue;

                if (result.Any(x => x.Key.Equals(BeautifyLabel(property.Name), StringComparison.OrdinalIgnoreCase)))
                    continue;

                var preview = ExtractPreview(property.Value);

                if (string.IsNullOrWhiteSpace(preview))
                    continue;

                result.Add(new ActivityHighlight
                {
                    Key = BeautifyLabel(property.Name),
                    Value = preview
                });
            }

            return result;
        }

        private static bool IsIgnoredProperty(string propertyName)
        {
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "type", "name", "displayName", "description",
            "activities", "connections", "metadata", "customProperties"
        };

            return ignored.Contains(propertyName);
        }

        private static string? ExtractPreview(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.String ||
                value.ValueKind == JsonValueKind.Number ||
                value.ValueKind == JsonValueKind.True ||
                value.ValueKind == JsonValueKind.False)
                return TrimPreview(value.ToString());

            if (value.ValueKind == JsonValueKind.Array)
            {
                var items = value.EnumerateArray()
                    .Take(4)
                    .Select(x => x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (items.Any())
                    return TrimPreview(string.Join(", ", items));
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("expression", out var expression))
                {
                    if (expression.ValueKind == JsonValueKind.Object && expression.TryGetProperty("value", out var expressionValue))
                        return TrimPreview(expressionValue.ToString());

                    return TrimPreview(expression.ToString());
                }

                if (value.TryGetProperty("value", out var rawValue))
                    return TrimPreview(rawValue.ToString());

                var compact = value.ToString();

                if (!string.IsNullOrWhiteSpace(compact))
                    return TrimPreview(compact);
            }

            return null;
        }

        private static string? GetEndpointActivityId(JsonElement connection, string propertyName)
        {
            if (!connection.TryGetProperty(propertyName, out var endpoint))
                return null;

            if (endpoint.ValueKind == JsonValueKind.String)
                return endpoint.GetString();

            if (endpoint.ValueKind != JsonValueKind.Object)
                return null;

            return FirstNonEmpty(
                GetString(endpoint, "activityId"),
                GetString(endpoint, "nodeId"),
                GetString(endpoint, "id"),
                GetString(endpoint, "activity"),
                GetNestedActivityId(endpoint, "activity"));
        }

        private static string? GetNestedActivityId(JsonElement endpoint, string propertyName)
        {
            if (!endpoint.TryGetProperty(propertyName, out var activity))
                return null;

            if (activity.ValueKind == JsonValueKind.String)
                return activity.GetString();

            if (activity.ValueKind == JsonValueKind.Object)
                return GetString(activity, "id");

            return null;
        }

        private static string? GetEndpointLabel(JsonElement connection, string propertyName)
        {
            if (!connection.TryGetProperty(propertyName, out var endpoint))
                return null;

            if (endpoint.ValueKind != JsonValueKind.Object)
                return null;

            return FirstNonEmpty(
                GetString(endpoint, "port"),
                GetString(endpoint, "outcome"),
                GetString(endpoint, "name"));
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (!element.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.ToString()
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        private static string? ShortType(string? fullType)
        {
            if (string.IsNullOrWhiteSpace(fullType))
                return null;

            var lastDot = fullType.LastIndexOf('.');
            return lastDot >= 0 ? fullType[(lastDot + 1)..] : fullType;
        }

        private static string TrimPreview(string? value, int maxLength = 80)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }

        private static string BeautifyLabel(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            if (raw.Equals("activities", StringComparison.OrdinalIgnoreCase))
                return "Lista principal";

            var chars = new List<char>();

            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];

                if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(raw[i - 1]))
                    chars.Add(' ');

                chars.Add(c);
            }

            var text = new string(chars.ToArray());
            return char.ToUpper(text[0]) + text[1..];
        }

        private static string DisplayOrDash(string? value)
            => string.IsNullOrWhiteSpace(value) ? "-" : value;

        private void ClearState()
        {
            StatusMessage = null;
            HasError = false;
            FormattedJson = null;
            Workflow = null;
            GraphNodes.Clear();
            GraphEdges.Clear();
            SelectedNodeId = null;
            _draggingNodeId = null;
            CanvasWidth = 1600;
            CanvasHeight = 900;
        }

        private sealed class WorkflowViewModel
        {
            public string? WorkflowId { get; set; }
            public string? DefinitionId { get; set; }
            public string? Name { get; set; }
            public string? RootId { get; set; }
            public string? RootType { get; set; }
            public string? RootDisplayName { get; set; }
            public List<ActivityNode> Activities { get; set; } = new();
            public List<ConnectionInfo> Connections { get; set; } = new();
        }

        private sealed class ActivityNode
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
            public string ShortType { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string? Description { get; set; }
            public string? SourceProperty { get; set; }
            public int Depth { get; set; }
            public int Order { get; set; }
            public List<ActivityHighlight> Highlights { get; set; } = new();
        }

        private sealed class ActivityHighlight
        {
            public string Key { get; set; } = "";
            public string Value { get; set; } = "";
        }

        private sealed class ConnectionInfo
        {
            public string SourceId { get; set; } = "";
            public string SourceLabel { get; set; } = "";
            public string TargetId { get; set; } = "";
            public string TargetLabel { get; set; } = "";
            public string? Outcome { get; set; }
        }

        private sealed class GraphNode
        {
            public string Id { get; set; } = "";
            public string Label { get; set; } = "";
            public string Type { get; set; } = "";
            public int Depth { get; set; }
            public string Kind { get; set; } = "generic";
            public double X { get; set; }
            public double Y { get; set; }
            public List<ActivityHighlight> Highlights { get; set; } = new();
        }

        private sealed class GraphEdge
        {
            public string SourceId { get; set; } = "";
            public string TargetId { get; set; } = "";
            public string? Outcome { get; set; }
        }

        private readonly record struct ActivityReference(JsonElement Element, string SourceProperty);
    }
}