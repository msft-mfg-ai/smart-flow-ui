// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Represents a folder node in a hierarchical folder structure
/// </summary>
public class FolderNode
{
    /// <summary>
    /// The name of the folder (not the full path, just the folder name)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The full path to this folder (e.g., "internal_docs/Terminal Unit (VAV)")
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Child folders
    /// </summary>
    public List<FolderNode> Children { get; set; } = new();

    /// <summary>
    /// Number of files directly in this folder (not including subfolders)
    /// </summary>
    public int FileCount { get; set; } = 0;

    /// <summary>
    /// Whether this folder is expanded in the UI
    /// </summary>
    public bool IsExpanded { get; set; } = false;

    public FolderNode()
    {
    }

    public FolderNode(string name, string path)
    {
        Name = name;
        Path = path;
    }
}

/// <summary>
/// Request model for creating a folder
/// </summary>
public class CreateFolderRequest
{
    /// <summary>
    /// The collection/container name
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// The folder path to create (e.g., "internal_docs/new_folder")
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;
}

/// <summary>
/// Request model for renaming a folder
/// </summary>
public class RenameFolderRequest
{
    /// <summary>
    /// The collection/container name
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// The current folder path
    /// </summary>
    public string OldFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// The new folder path
    /// </summary>
    public string NewFolderPath { get; set; } = string.Empty;
}

/// <summary>
/// Request model for deleting a folder
/// </summary>
public class DeleteFolderRequest
{
    /// <summary>
    /// The collection/container name
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// The folder path to delete
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;
}
