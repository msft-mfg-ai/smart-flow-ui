using System.Text;
using Microsoft.Extensions.AI;

namespace MinimalApi.Agents;

/// <summary>
/// Extensions for building chat messages with file uploads using Microsoft Agent Framework
/// </summary>
public static class ChatHistoryExtensions
{
    /// <summary>
    /// Creates a list of chat messages including the user message with any file uploads.
    /// Files are converted to appropriate content types (images, extracted PDF text, etc.)
    /// </summary>
    public static IList<ChatMessage> CreateUserMessageWithUploads(
        string userMessage,
        IEnumerable<FileSummary>? fileUploads)
    {
        var messages = new List<ChatMessage>();

        if (fileUploads != null && fileUploads.Any())
        {
            var contentParts = new List<AIContent>();
            
            // Add the text message
            contentParts.Add(new TextContent(userMessage));

            foreach (var file in fileUploads)
            {
                DataUriParser parser = new DataUriParser(file.DataUrl);
                
                if (parser.MediaType == "image/jpeg" || parser.MediaType == "image/png")
                {
                    // Add image content
                    contentParts.Add(new DataContent(parser.Data, parser.MediaType));
                }
                else if (parser.MediaType == "application/pdf")
                {
                    // Extract text from PDF and add as text content
                    string pdfData = PDFTextExtractor.ExtractTextFromPdf(parser.Data);
                    contentParts.Add(new TextContent($"[PDF Content from {file.FileName}]:\n{pdfData}"));
                }
                else
                {
                    // Handle CSV and other text-based files
                    string fileData = Encoding.UTF8.GetString(parser.Data);
                    contentParts.Add(new TextContent($"[File Content from {file.FileName}]:\n{fileData}"));
                }
            }

            messages.Add(new ChatMessage(ChatRole.User, contentParts));
        }
        else
        {
            messages.Add(new ChatMessage(ChatRole.User, userMessage));
        }

        return messages;
    }

    /// <summary>
    /// Adds a user message with file uploads to an existing list of chat messages.
    /// </summary>
    public static void AddUserMessageWithUploads(
        this IList<ChatMessage> messages,
        string userMessage,
        IEnumerable<FileSummary>? fileUploads)
    {
        var newMessages = CreateUserMessageWithUploads(userMessage, fileUploads);
        foreach (var message in newMessages)
        {
            messages.Add(message);
        }
    }

    /// <summary>
    /// Converts chat history (ChatTurn array) to a list of ChatMessages for the Agent Framework.
    /// </summary>
    public static IList<ChatMessage> ToChatMessages(this ChatTurn[] history)
    {
        var messages = new List<ChatMessage>();

        foreach (var turn in history)
        {
            if (!string.IsNullOrEmpty(turn.User))
            {
                messages.Add(new ChatMessage(ChatRole.User, turn.User));
            }
            
            if (!string.IsNullOrEmpty(turn.Assistant))
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, turn.Assistant));
            }
        }

        return messages;
    }

    /// <summary>
    /// Creates a system message for the Agent Framework.
    /// </summary>
    public static ChatMessage CreateSystemMessage(string systemPrompt)
    {
        return new ChatMessage(ChatRole.System, systemPrompt);
    }
}
