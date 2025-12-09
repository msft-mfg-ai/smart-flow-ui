// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

public record class ChatRequest(
    Guid ChatId,
    Guid ChatTurnId,
    ChatTurn[] History,
    IEnumerable<string> SelectedUserCollectionFiles,
    IEnumerable<FileSummary> FileUploads,
    Dictionary<string, string> OptionFlags,
    UserSelectionModel? UserSelectionModel,
    string? ThreadId = null,
    RequestOverrides? Overrides = null)
{
    public string? LastUserQuestion => History?.LastOrDefault()?.User;
}

public record class ChatRatingRequest(Guid ChatId, Guid MessageId, int Rating, string Feedback);

public class RequestDiagnosticsBuilder
{
    // Aggregate all the function call results
    public List<ExecutionStepResult> FunctionCallResults = new();

    public void AddFunctionCallResult(string name, string result)
    {
        FunctionCallResults.Add(new ExecutionStepResult(name, result));
    }

    public void AddFunctionCallResult(string name, string result, List<SupportingContentRecord> sources)
    {
        FunctionCallResults.Add(new ExecutionStepResult(name, result, sources));
    }
}

public record ExecutionStepResult(string Name, string Result, List<SupportingContentRecord>? Sources = null);
