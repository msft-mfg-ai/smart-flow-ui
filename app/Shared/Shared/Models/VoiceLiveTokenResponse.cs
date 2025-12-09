// Copyright (c) Microsoft. All rights reserved.

namespace Shared.Models;

/// <summary>
/// Response model for Voice Live authentication tokens and configuration
/// </summary>
public record VoiceLiveTokenResponse(
    string WebSocketUrl,
    string ApiVersion,
    string AgentId,
    string ProjectName,
    string AgentAccessToken,
    string AuthorizationToken,
    string SpeechKey,
    string SpeechRegion
);
