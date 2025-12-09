// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Represents metadata information for a collection
/// </summary>
public class CollectionInfo
{
    /// <summary>
    /// The name of the collection (container name)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the collection
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional type/category of the collection
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Optional Azure AI Search index name associated with this collection
    /// </summary>
    public string? IndexName { get; set; }

    public CollectionInfo()
    {
    }

    public CollectionInfo(string name, string? description = null, string? type = null, string? indexName = null)
    {
        Name = name;
        Description = description;
        Type = type;
        IndexName = indexName;
    }
}

/// <summary>
/// Request model for creating a collection with metadata
/// </summary>
public class CreateCollectionRequest
{
    /// <summary>
    /// The name of the collection to create
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the collection
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional type/category of the collection
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Optional Azure AI Search index name to associate with this collection
    /// </summary>
    public string? IndexName { get; set; }
}
