// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Represents a file in a container along with its associated processing files
/// </summary>
public class ContainerFileInfo
{
    /// <summary>
    /// The name of the main file in the container
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the file
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The folder path within the collection (e.g., "internal_docs/Terminal Unit (VAV)")
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Collection of processing files associated with the main file from the extract container
    /// </summary>
    public List<string> ProcessingFiles { get; set; } = new();

    /// <summary>
    /// Metadata associated with the file
    /// </summary>
    public FileMetadata? Metadata { get; set; }

    public ContainerFileInfo()
    {
    }

    public ContainerFileInfo(string fileName, string? description = null)
    {
        FileName = fileName;
        Description = description;
    }

    public ContainerFileInfo(string fileName, string? description, List<string> processingFiles)
    {
        FileName = fileName;
        Description = description;
        ProcessingFiles = processingFiles;
    }

    public ContainerFileInfo(string fileName, string folderPath, FileMetadata? metadata = null)
    {
        FileName = fileName;
        FolderPath = folderPath;
        Metadata = metadata;
    }
}
