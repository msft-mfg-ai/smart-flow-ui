// Copyright (c) Microsoft. All rights reserved.

using Newtonsoft.Json;

namespace Shared.Models;

/// <summary>
/// Build Info
/// </summary>
public class BuildInfo
{
    public static readonly BuildInfo Instance = Create();

    /// <summary>
    /// Build Date
    /// </summary>
    [JsonProperty("buildDate")]
    public string BuildDate { get; set; }

    /// <summary>
    /// Build Number
    /// </summary>
    [JsonProperty("buildNumber")]
    public string BuildNumber { get; set; }

    /// <summary>
    /// Build Id
    /// </summary>
    [JsonProperty("buildId")]
    public string BuildId { get; set; }

    /// <summary>
    /// Branch Name
    /// </summary>
    [JsonProperty("branchName")]
    public string BranchName { get; set; }

    /// <summary>
    /// Commit Hash
    /// </summary>
    [JsonProperty("commitHash")]
    public string CommitHash { get; set; }

    /// <summary>
    /// Image Tag
    /// </summary>
    [JsonProperty("imageTag")]
    public string ImageTag { get; set; }

    /// <summary>
    /// Constructor
    /// </summary>
    private BuildInfo()
    {
        BuildDate = string.Empty;
        BuildNumber = string.Empty;
        BuildId = string.Empty;
        BranchName = string.Empty;
        CommitHash = string.Empty;
        ImageTag = string.Empty;
    }

    public override string ToString()
    {
        return $"Build Date: {BuildDate}, Build Number: {BuildNumber}, Build Id: {BuildId}, Image Tag: {ImageTag}, Branch Name: {BranchName}, Commit Hash: {CommitHash}";
    }

    /// <summary>
    /// Constructor
    /// </summary>
    private BuildInfo(string buildDate, string buildNumber, string buildId, string branchName, string commitHash, string imageTag)
    {
        BuildDate = buildDate;
        BuildNumber = buildNumber;
        BuildId = buildId;
        BranchName = branchName;
        CommitHash = commitHash;
        ImageTag = imageTag;
    }

    private static BuildInfo Create()
    {
        try
        {
            var assembly = typeof(BuildInfo).Assembly!;
            string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("buildinfo.json"));
            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using StreamReader reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var buildInfo = JsonConvert.DeserializeObject<BuildInfo>(json);
            return buildInfo ?? new BuildInfo();
        }
        catch
        {
            return new BuildInfo
            {
                BuildDate = string.Empty,
                BuildNumber = string.Empty,
                BuildId = string.Empty,
                BranchName = string.Empty,
                CommitHash = string.Empty,
                ImageTag = string.Empty
            };
        }

    }
}
