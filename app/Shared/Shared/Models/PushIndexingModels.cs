// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Shared.Models;

/// <summary>
/// Request model for triggering push-based indexing via Agent-Hub-JCI
/// </summary>
public class PushIndexingRequest
{
    /// <summary>
    /// Name of the blob container to index. If not provided, uses the default container.
    /// </summary>
    public string? ContainerName { get; set; }

    /// <summary>
    /// Whether to recreate the search index from scratch. Default is false (incremental update).
    /// </summary>
    public bool RecreateIndex { get; set; } = false;

    public PushIndexingRequest()
    {
    }

    public PushIndexingRequest(string containerName, bool recreateIndex = false)
    {
        ContainerName = containerName;
        RecreateIndex = recreateIndex;
    }
}

/// <summary>
/// Response model from push-based indexing trigger
/// </summary>
public class PushIndexingResponse
{
    /// <summary>
    /// Unique correlation ID for tracking the indexing job
    /// </summary>
    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable message about the indexing operation
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the job (e.g., "queued", "running", "completed", "failed")
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Response model for checking push indexing job status
/// </summary>
public class PushIndexingStatusResponse
{
    /// <summary>
    /// Unique correlation ID for the indexing job
    /// </summary>
    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the job (e.g., "queued", "running", "completed", "failed")
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Number of documents processed so far
    /// </summary>
    [JsonPropertyName("progress_count")]
    public int ProgressCount { get; set; }

    /// <summary>
    /// Total number of documents to process
    /// </summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    /// <summary>
    /// Human-readable message about the current state
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Error message if the job failed
    /// </summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}
