// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Assistant panel's conversation (specs/098): the questions asked and the replies, for the
// running session only — never written to the project or to disk. A reply comes from an
// IAssistantResponder; this feature's is ManualResponder (quotes from the user manual), and
// #607 adds a language model behind the same interface without touching the panel.

namespace Nota.Application.Assistant;

public enum ChatRole { User, Assistant }

/// <summary>A how-to's anchored steps, offered under a reply as Show me.</summary>
public sealed record Walkthrough(ManualSection Section, IReadOnlyList<AnchoredStep> Steps);

/// <summary>One message. A reply's <see cref="Markdown"/> refers to its walkthroughs as
/// <c>nota:showme/&lt;n&gt;</c> links, n indexing <see cref="Walkthroughs"/>.</summary>
public sealed record ChatMessage(
    ChatRole Role,
    string Markdown,
    IReadOnlyList<ManualSection> Quoted,
    IReadOnlyList<Walkthrough> Walkthroughs,
    DateTime Time)
{
    public static ChatMessage FromUser(string text) => new(ChatRole.User, text, [], [], DateTime.Now);
}

/// <summary>Answers a question. Called off the UI thread.</summary>
public interface IAssistantResponder
{
    Task<ChatMessage> ReplyAsync(string question, CancellationToken ct = default);
}

/// <summary>The session's messages, oldest first.</summary>
public sealed class Conversation
{
    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Raised after a message is added or the conversation is cleared.</summary>
    public event Action? Changed;

    public void Add(ChatMessage message)
    {
        _messages.Add(message);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_messages.Count == 0) return;
        _messages.Clear();
        Changed?.Invoke();
    }
}
