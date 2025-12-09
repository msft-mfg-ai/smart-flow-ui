# Profile Configuration Guide

## Overview

Profiles define the behavior, capabilities, and configuration of agents in the SmartFlow UI application. Each profile represents a specific AI assistant that users can interact with, whether it's a general-purpose chatbot, a RAG (Retrieval-Augmented Generation) agent with access to specific knowledge bases, or an external API-based workflow.

---

## Table of Contents

- [Profile Structure](#profile-structure)
- [Profile Loading Priority](#profile-loading-priority)
- [Configuration Methods](#configuration-methods)
  - [1. Embedded Resource (Default)](#1-embedded-resource-default)
  - [2. Azure Blob Storage](#2-azure-blob-storage)
  - [3. Environment Variable (Base64)](#3-environment-variable-base64)
- [Profile Types](#profile-types)
  - [Chat Profile](#chat-profile)
  - [RAG Profile](#rag-profile)
  - [Endpoint Assistant Profile](#endpoint-assistant-profile)
- [Security Models](#security-models)
- [System Prompts](#system-prompts)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

---

## Profile Structure

A profile is defined as a JSON object with the following properties:

```json
{
  "Name": "Profile Display Name",
  "Id": "UniqueProfileId",
  "Approach": "Chat|RAG|ENDPOINTASSISTANT|ENDPOINTASSISTANTTASK",
  "SecurityModel": "None|GroupMembership",
  "SecurityModelGroupMembership": ["Group1", "Group2"],
  "AllowFileUpload": true|false,
  "ChatSystemMessageFile": "PromptFileName",
  "SampleQuestions": [
    "Example question 1",
    "Example question 2"
  ],
  "RAGSettings": { /* RAG-specific settings */ },
  "AssistantEndpointSettings": { /* Endpoint-specific settings */ },
  "AzureAIAgentID": "optional-agent-id"
}
```

### Core Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Display name shown to users |
| `Id` | string | Yes | Unique identifier for the profile |
| `Approach` | string | Yes | Type of agent: `Chat`, `RAG`, `ENDPOINTASSISTANT`, `ENDPOINTASSISTANTTASK` |
| `SecurityModel` | string | Yes | Access control: `None` or `GroupMembership` |
| `SecurityModelGroupMembership` | array | No | List of groups allowed to access this profile |
| `AllowFileUpload` | boolean | No | Whether users can upload files to this agent |
| `ChatSystemMessageFile` | string | No | Name of the system prompt file (without extension) |
| `SampleQuestions` | array | No | Pre-defined example questions for users |
| `AzureAIAgentID` | string | No | Azure AI Agent ID for agent-based profiles |

---

## Profile Loading Priority

The application loads profiles from the first available source in this priority order:

### 1. **Azure Blob Storage** (Highest Priority)
   - Environment Variable: `ProfileConfigurationBlobStorageContainer`
   - File: `profiles.json` in the specified container
   - **Best for:** Production environments where profiles need to be updated without redeployment

### 2. **Environment Variable Configuration**
   - Environment Variable: `ProfileConfiguration`
   - Format: Base64-encoded JSON string
   - **Best for:** Azure App Service, Container Apps, or Kubernetes deployments

### 3. **Embedded Resource** (Default Fallback)
   - Location: `SmartFlow.UI.API\Services\Profile\profiles.json`
   - Compiled into the application as an embedded resource
   - File name configurable via `ProfileFileName` setting (defaults to "profiles")
   - **Best for:** Development and testing

---

## Configuration Methods

### 1. Embedded Resource (Default)

The embedded resource is the default configuration method used during development and serves as a fallback if no other configuration is specified.

**Location:** `SmartFlow.UI.API\Services\Profile\profiles.json`

**Configuration in AppConfiguration:**
```json
{
  "ProfileFileName": "profiles"  // Default value, can be changed
}
```

**How it works:**
- The `profiles.json` file is marked as an embedded resource in the project
- The application loads it using `Assembly.GetManifestResourceStream()`
- Resource name format: `SmartFlow.UI.API.Services.Profile.{ProfileFileName}.json`

**Advantages:**
- ✅ Simple for local development
- ✅ No external dependencies
- ✅ Version controlled with source code
- ✅ Always available as a fallback

**Limitations:**
- ❌ Requires redeployment to update profiles
- ❌ Not suitable for frequently changing configurations

---

### 2. Azure Blob Storage

Store your `profiles.json` file in an Azure Blob Storage container for dynamic updates without redeployment.

**Setup:**

1. Create a container in your Azure Storage Account (e.g., `profile-config`)
2. Upload your `profiles.json` file to the container
3. Set the environment variable:

```json
{
  "ProfileConfigurationBlobStorageContainer": "profile-config"
}
```

**How it works:**
- The application connects to the blob storage using the configured `BlobServiceClient`
- Downloads `profiles.json` from the specified container
- Caches the profiles in memory until manually reloaded

**Advantages:**
- ✅ Update profiles without redeploying the application
- ✅ Centralized configuration for multiple instances
- ✅ Version history through blob storage
- ✅ Easy to manage via Azure Portal or CLI

**Authentication:**
- Uses the same `BlobServiceClient` configured for the application
- Supports both connection strings and Managed Identity

**Reloading:**
- Profiles can be reloaded via the Settings page in the UI
- Navigate to: **Settings → Profile Config → Reload Profile Data**

---

### 3. Environment Variable (Base64)

Store profiles as a Base64-encoded JSON string in an environment variable for containerized deployments.

**Setup:**

1. Create your `profiles.json` file
2. Encode it to Base64:
   ```bash
   # Linux/Mac
   base64 -i profiles.json -o profiles.txt
   
   # Windows PowerShell
   [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((Get-Content -Raw profiles.json)))
   ```
3. Set the environment variable:

```json
{
  "ProfileConfiguration": "<base64-encoded-json-string>"
}
```

**UI Helper:**
- Use the Settings page to encode/decode Base64 strings
- Navigate to: **Settings → Converter**

**How it works:**
- The application reads the `ProfileConfiguration` environment variable
- Decodes the Base64 string to get the JSON content
- Deserializes the JSON into profile definitions

**Advantages:**
- ✅ Works with any deployment platform
- ✅ No external storage dependencies
- ✅ Suitable for containerized environments
- ✅ Can be set in Azure App Service configuration

**Limitations:**
- ❌ Environment variable size limits may apply
- ❌ Less readable than file-based configurations
- ❌ Requires Base64 encoding/decoding

