// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Represents information about an Azure AI Search index
/// </summary>
public class SearchIndexInfo
{
    /// <summary>
    /// The name of the index
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of fields in the index
    /// </summary>
    public List<SearchIndexField> Fields { get; set; } = new();

    /// <summary>
    /// Number of documents in the index
    /// </summary>
    public long? DocumentCount { get; set; }

    /// <summary>
    /// Storage size in bytes
    /// </summary>
    public long? StorageSize { get; set; }

    public SearchIndexInfo()
    {
    }

    public SearchIndexInfo(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Represents a field in an Azure AI Search index
/// </summary>
public class SearchIndexField
{
    /// <summary>
    /// The name of the field
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The data type of the field
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether the field is searchable
    /// </summary>
    public bool IsSearchable { get; set; }

    /// <summary>
    /// Whether the field is filterable
    /// </summary>
    public bool IsFilterable { get; set; }

    /// <summary>
    /// Whether the field is sortable
    /// </summary>
    public bool IsSortable { get; set; }

    /// <summary>
    /// Whether the field is facetable
    /// </summary>
    public bool IsFacetable { get; set; }

    /// <summary>
    /// Whether the field is a key field
    /// </summary>
    public bool IsKey { get; set; }

    /// <summary>
    /// Whether the field is retrievable
    /// </summary>
    public bool IsRetrievable { get; set; }

    public SearchIndexField()
    {
    }

    public SearchIndexField(string name, string type)
    {
        Name = name;
        Type = type;
    }
}
