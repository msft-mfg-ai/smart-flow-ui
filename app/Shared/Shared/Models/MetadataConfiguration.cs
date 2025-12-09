// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Configuration for a metadata field
/// </summary>
public class MetadataFieldConfiguration
{
    /// <summary>
    /// The property name in FileMetadata (e.g., "EquipmentCategory")
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the field (e.g., "Equipment Category")
    /// </summary>
    public string DisplayLabel { get; set; } = string.Empty;

    /// <summary>
    /// Field type (text, dropdown, boolean)
    /// </summary>
    public MetadataFieldType FieldType { get; set; } = MetadataFieldType.Text;

    /// <summary>
    /// Whether this field is required
    /// </summary>
    public bool IsRequired { get; set; } = false;

    /// <summary>
    /// Whether to show this field in the file list table
    /// </summary>
    public bool ShowInTable { get; set; } = false;

    /// <summary>
    /// Display order in forms and tables (lower numbers appear first)
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Options for dropdown fields
    /// </summary>
    public List<string> DropdownOptions { get; set; } = new();

    /// <summary>
    /// Placeholder text for the field
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Help text or description for the field
    /// </summary>
    public string? HelpText { get; set; }

    /// <summary>
    /// Maximum length for text fields
    /// </summary>
    public int? MaxLength { get; set; }
}

/// <summary>
/// Type of metadata field
/// </summary>
public enum MetadataFieldType
{
    Text,
    Dropdown,
    Boolean
}

/// <summary>
/// Complete metadata configuration for a collection or globally
/// </summary>
public class MetadataConfiguration
{
    /// <summary>
    /// Configuration name or identifier
    /// </summary>
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Description of this configuration
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// List of field configurations
    /// </summary>
    public List<MetadataFieldConfiguration> Fields { get; set; } = new();
}
