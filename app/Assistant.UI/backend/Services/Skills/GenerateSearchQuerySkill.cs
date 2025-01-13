﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using Microsoft.SemanticKernel.ChatCompletion;
using MinimalApi.Extensions;
using MinimalApi.Services.Profile.Prompts;

namespace MinimalApi.Services.Skills;


public sealed class GenerateSearchQuerySkill
{
    [KernelFunction("GenerateSearchQuery"), Description("Generate a search query for user question.")]
    public async Task GenerateSearchQueryAsync([Description("chat History")] ChatTurn[] chatTurns,
                                               KernelArguments arguments,
                                               Kernel kernel)
    {
        var chatGpt = kernel.Services.GetService<IChatCompletionService>();
        arguments[ContextVariableOptions.ResponsibleAIPolicyViolation] = false;
        try
        {
            // Build chat history
            var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory(PromptService.GetPromptByName(PromptService.RAGSearchSystemPrompt)).AddChatHistory(chatTurns);
            var userMessage = await PromptService.RenderPromptAsync(kernel, PromptService.GetPromptByName(PromptService.RAGSearchUserPrompt), arguments);
            chatHistory.AddUserMessage(userMessage);

            // Get search query from LLM
            var searchAnswer = await chatGpt.GetChatMessageContentAsync(chatHistory, DefaultSettings.AISearchRequestSettings, kernel);
            arguments[ContextVariableOptions.SearchQuery] = searchAnswer.Content;

            // Build diagnostics for search request
            chatHistory.AddAssistantMessage(searchAnswer.Content);
            var requestProperties = chatHistory.GenerateRequestProperties(DefaultSettings.AIChatRequestSettings);
            arguments[ContextVariableOptions.SearchDiagnostics] = requestProperties;
        }
        catch (Microsoft.SemanticKernel.HttpOperationException ex)
        {
            if (((Azure.RequestFailedException)ex.InnerException).ErrorCode == "content_filter")
            {
                arguments[ContextVariableOptions.ResponsibleAIPolicyViolation] = true;
            }
        }
    }
}
