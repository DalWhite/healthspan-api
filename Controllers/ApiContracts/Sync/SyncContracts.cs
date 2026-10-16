using System.Text.Json;
using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.Sync;

public sealed class SyncRequest
{
    public List<SyncOperationPayload> Operations { get; set; } = [];
}

public sealed class SyncOperationPayload
{
    [JsonPropertyName("opId")]
    public Guid OpId { get; set; }

    [JsonPropertyName("entity")]
    public string Entity { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("clientEntityId")]
    public Guid? ClientEntityId { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

public sealed class SyncResponse
{
    public int Applied { get; set; }

    public int Deduped { get; set; }

    public List<SyncFailure> Failed { get; set; } = [];

    public List<SyncIdMap> IdMap { get; set; } = [];
}

public sealed class SyncFailure
{
    public Guid OpId { get; set; }

    public string? Reason { get; set; }
}

public sealed class SyncIdMap
{
    public Guid ClientId { get; set; }

    public Guid ServerId { get; set; }
}
