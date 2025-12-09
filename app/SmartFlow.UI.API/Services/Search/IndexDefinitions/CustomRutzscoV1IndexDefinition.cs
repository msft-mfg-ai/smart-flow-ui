// Copyright (c) Microsoft. All rights reserved.

namespace MinimalApi.Services.Search.IndexDefinitions;

public class CustomRutzscoV1IndexDefinition : IKnowledgeSource
{
    public required string id { get; set; }

    public required string content { get; set; }

    public required string metadata { get; set; }

    public KnowledgeSource GetSource(bool useSourcepage = false)
    {
        return new KnowledgeSource(GetFilepath(useSourcepage), content);
    }

    public string GetFilepath(bool useSourcepage = false)
    {
        // Use metadata as the source identifier, fallback to id if metadata is empty
        return !string.IsNullOrEmpty(metadata) ? metadata : id;
    }

    public static string EmbeddingsFieldName = "content_vector";
    public static List<string> SelectFieldNames = new List<string> { "id", "content", "metadata" };
    public static string Name = "CustomRutzscoV1";
}
