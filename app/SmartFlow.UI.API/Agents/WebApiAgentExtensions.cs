// Copyright (c) Microsoft. All rights reserved.
using Shared.Models;
using Azure;
using Azure.Identity;
using Azure.AI.OpenAI;

namespace MinimalApi.Extensions;

internal static class WebApiAgentExtensions
{
    internal static WebApplication MapAgentManagementApi(this WebApplication app)
    {
        var api = app.MapGroup("api");

        // Agent endpoints
        api.MapGet("agents", OnGetAgentsAsync);
        api.MapGet("agents/{agentId}", OnGetAgentAsync);
        api.MapPost("agent", OnCreateAgentAsync);
        api.MapPut("agent/{agentId}", OnUpdateAgentAsync);
        api.MapDelete("agents/{agentName}", OnDeleteAgentsByNameAsync);

        // On-demand image fetch endpoint
        api.MapGet("images/{fileId}", OnGetAgentImageAsync);

        return app;
    }

    private static async Task<IResult> OnGetAgentsAsync(HttpContext context, MinimalApi.Agents.IAgentManagementService service)
    {
        var agents = await service.ListAgentsAsync();
        return Results.Ok(agents);
    }

    private static async Task<IResult> OnGetAgentAsync(HttpContext context, string agentId, MinimalApi.Agents.IAgentManagementService service)
    {
        try
        {
            var agent = await service.GetAgentAsync(agentId);
            return Results.Ok(agent);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Results.Problem($"An error occurred while retrieving the agent: {ex.Message}");
        }
    }

    private static async Task<IResult> OnCreateAgentAsync(AgentViewModel agentViewModel, MinimalApi.Agents.IAgentManagementService service, HttpContext context)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(agentViewModel.Name))
        {
            return Results.BadRequest("Agent Name is required.");
        }

        if (string.IsNullOrWhiteSpace(agentViewModel.Instructions))
        {
            return Results.BadRequest("Agent Instructions are required.");
        }

        try
        {
            var model = !string.IsNullOrWhiteSpace(agentViewModel.Model) ? agentViewModel.Model : "gpt-4o";
            var createdAgent = await service.CreateAgentAsync(
                agentViewModel.Name, 
                agentViewModel.Instructions, 
                agentViewModel.Description,
                model);
            
            return Results.Created($"/api/agents/{createdAgent.Id}", createdAgent);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Results.Problem($"An error occurred while creating the agent: {ex.Message}");
        }
    }

    private static async Task<IResult> OnUpdateAgentAsync(string agentId, AgentViewModel agentViewModel, MinimalApi.Agents.IAgentManagementService service, HttpContext context)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return Results.BadRequest("Agent ID is required.");
        }

        if (string.IsNullOrWhiteSpace(agentViewModel.Name))
        {
            return Results.BadRequest("Agent Name is required.");
        }

        if (string.IsNullOrWhiteSpace(agentViewModel.Instructions))
        {
            return Results.BadRequest("Agent Instructions are required.");
        }

        try
        {
            var model = !string.IsNullOrWhiteSpace(agentViewModel.Model) ? agentViewModel.Model : "gpt-4o";
            var updatedAgent = await service.UpdateAgentAsync(
                agentId, 
                agentViewModel.Name, 
                agentViewModel.Instructions, 
                agentViewModel.Description, 
                model);
            
            return Results.Ok(updatedAgent);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Results.Problem($"An error occurred while updating the agent: {ex.Message}");
        }
    }

    private static Task<IResult> OnGetAgentImageAsync(string fileId, IConfiguration config, CancellationToken cancellationToken)
    {
        // For Microsoft Agent Framework, images are typically returned as URLs or base64 directly
        // This endpoint is a placeholder - actual implementation depends on how images are stored
        
        // Return a not implemented response for now
        // In production, you would integrate with your blob storage or image service
        return Task.FromResult(Results.NotFound($"Image with fileId '{fileId}' not found. Agent Framework uses direct image URLs."));
    }

    private static string? GuessContentType(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            _ => null
        };
    }

    private static async Task<IResult> OnDeleteAgentsByNameAsync(string agentName, MinimalApi.Agents.IAgentManagementService service, HttpContext context)
    {
        try
        {
            var deletedCount = await service.DeleteAgentsByNameAsync(agentName);
            return Results.Ok(new { DeletedCount = deletedCount, Message = $"Deleted {deletedCount} agent(s) with name '{agentName}'" });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Results.Problem($"An error occurred while deleting agents: {ex.Message}");
        }
    }
}

