// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Represents metadata for a file or folder in the collection
/// </summary>
public class FileMetadata
{
    /// <summary>
    /// The name of the file (e.g., "130.13-EG1 (0521) TSS Series Engineering Guide.pdf")
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The blob storage path (e.g., "internal_docs/Terminal Unit (VAV)/130.13-EG1 (0521) TSS Series Engineering Guide.pdf")
    /// </summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>
    /// Equipment category classification
    /// </summary>
    public string EquipmentCategory { get; set; } = string.Empty;

    /// <summary>
    /// Equipment subcategory classification
    /// </summary>
    public string EquipmentSubcategory { get; set; } = string.Empty;

    /// <summary>
    /// Equipment part identifier
    /// </summary>
    public string EquipmentPart { get; set; } = string.Empty;

    /// <summary>
    /// Equipment part subcategory
    /// </summary>
    public string EquipmentPartSubcategory { get; set; } = string.Empty;

    /// <summary>
    /// Product name or identifier
    /// </summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>
    /// Manufacturer name
    /// </summary>
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>
    /// Document type (e.g., "Guide Specs", "Manual", "Datasheet")
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// Whether the document is required for CDE (Common Data Environment)
    /// </summary>
    public string IsRequiredForCde { get; set; } = "No";

    /// <summary>
    /// Whether the document has been added to the search index
    /// </summary>
    public string AddedToIndex { get; set; } = "No";

    public FileMetadata()
    {
    }

    public FileMetadata(string fileName, string blobPath)
    {
        FileName = fileName;
        BlobPath = blobPath;
    }
}

/// <summary>
/// Request model for updating file metadata
/// </summary>
public class UpdateFileMetadataRequest
{
    public string FileName { get; set; } = string.Empty;
    public FileMetadata Metadata { get; set; } = new();
}
