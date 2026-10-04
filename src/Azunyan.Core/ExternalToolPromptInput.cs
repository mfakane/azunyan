namespace Azunyan.Core;

/// <summary>
/// The submitted prompt text and accumulated inline-chat history serialized as
/// a JSON array of role/content messages.
/// </summary>
public sealed record ExternalToolPromptInput(string Input, string ChatHistory);
