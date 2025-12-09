// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Represents the overall status of a workflow
/// </summary>
public enum WorkflowState
{
    NotStarted,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// Represents the status of an individual workflow step
/// </summary>
public enum StepState
{
    Pending,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// Represents a single step in a workflow
/// </summary>
public class WorkflowStep
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public StepState State { get; set; } = StepState.Pending;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents the complete status of a document processing workflow
/// </summary>
public class WorkflowStatus
{
    public string WorkflowId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public WorkflowState State { get; set; } = WorkflowState.NotStarted;
    public List<WorkflowStep> Steps { get; set; } = new();
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// Creates a sample workflow status for demonstration
    /// </summary>
    public static WorkflowStatus CreateSample(string fileName, WorkflowState state = WorkflowState.InProgress)
    {
        var workflow = new WorkflowStatus
        {
            WorkflowId = Guid.NewGuid().ToString(),
            FileName = fileName,
            State = state,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            Steps = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Name = "Extract Text",
                    Description = "Extracting text content from document",
                    State = StepState.Completed,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow.AddMinutes(-4)
                },
                new WorkflowStep
                {
                    Name = "Parse Tables",
                    Description = "Analyzing and extracting table structures",
                    State = StepState.Completed,
                    StartTime = DateTime.UtcNow.AddMinutes(-4),
                    EndTime = DateTime.UtcNow.AddMinutes(-3)
                },
                new WorkflowStep
                {
                    Name = "Generate Embeddings",
                    Description = "Creating vector embeddings for semantic search",
                    State = state == WorkflowState.Completed ? StepState.Completed : StepState.InProgress,
                    StartTime = DateTime.UtcNow.AddMinutes(-3),
                    EndTime = state == WorkflowState.Completed ? DateTime.UtcNow.AddMinutes(-1) : null
                },
                new WorkflowStep
                {
                    Name = "Update Index",
                    Description = "Updating search index with processed content",
                    State = state == WorkflowState.Completed ? StepState.Completed : StepState.Pending,
                    StartTime = state == WorkflowState.Completed ? DateTime.UtcNow.AddMinutes(-1) : null,
                    EndTime = state == WorkflowState.Completed ? DateTime.UtcNow : null
                }
            }
        };

        if (state == WorkflowState.Completed)
        {
            workflow.EndTime = DateTime.UtcNow;
        }

        // Calculate progress
        var completedSteps = workflow.Steps.Count(s => s.State == StepState.Completed);
        workflow.ProgressPercentage = (int)((double)completedSteps / workflow.Steps.Count * 100);

        return workflow;
    }
}
