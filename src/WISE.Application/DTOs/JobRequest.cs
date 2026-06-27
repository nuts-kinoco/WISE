using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using WISE.Domain.Enums;

namespace WISE.Application.DTOs;

public class JobRequest
{
    public string JobType { get; set; } = string.Empty; // e.g. "Import", "MetadataRefresh"

    // Holds the dynamic payload (e.g., ImportJobRequest) as JSON element
    public JsonElement? Payload { get; set; }

    public int Priority { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JobStatus Status { get; set; } = JobStatus.Created;
}
