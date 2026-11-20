using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Services.Chat;

public sealed record ChatMessage(string Role, string Content);
public sealed record ModelChatReply(string Text, string? Expression = null);
public sealed record CompanionChatReply(string Text, Guid? ExpressionSceneId);

public interface IChatEngine
{
    Task<string> ReplyAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken token);
    async Task<ModelChatReply> ReplyWithExpressionAsync(string modelId, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<string> expressions, CancellationToken token) =>
        new(await ReplyAsync(modelId, messages, token));
}

/// <summary>Switching identity or disabling chat invalidates the entire in-flight turn.</summary>
public sealed class ChatSession(ChatHistoryStore history, IChatEngine engine)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private Guid? _selectedId;

    public void Select(Guid? id)
    {
        lock (_gate)
        {
            _selectedId = id;
            _pending?.Cancel();
        }
    }

    public async Task<string> SendAsync(CompanionDefinition profile, string modelId, string prompt, CancellationToken token)
        => (await SendReplyAsync(profile, modelId, prompt, token)).Text;

    public async Task<CompanionChatReply> SendReplyAsync(CompanionDefinition profile, string modelId, string prompt, CancellationToken token)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_gate)
        {
            if (_selectedId != profile.Id)
                throw new OperationCanceledException();
            if (_pending is { IsCancellationRequested: false })
                throw new InvalidOperationException("A chat turn is already in progress.");
            _pending = request;
        }
        try
        {
            var saved = history.ForPet(profile.Id);
            var messages = BuildMessages(profile, saved, prompt);
            var expressions = ChatExpressionPolicy.Available(profile.ActiveScenes);
            var generated = expressions.Count == 0
                ? new ModelChatReply(await engine.ReplyAsync(modelId, messages, request.Token))
                : await engine.ReplyWithExpressionAsync(modelId, messages, expressions.Keys.ToArray(), request.Token);
            var reply = generated.Text.Trim();
            if (reply.Length == 0)
                throw new InvalidDataException("Model returned an empty reply.");
            lock (_gate)
            {
                request.Token.ThrowIfCancellationRequested();
                var prefix = LocalizationService.Get("Chat.RememberPrefix") + " ";
                var note = prompt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? prompt[prefix.Length..].Trim() : null;
                history.Append(profile.Id, profile.Name, new(prompt, reply, DateTimeOffset.Now), note);
            }
            return new(reply, generated.Expression is { } key && expressions.TryGetValue(key, out var id) ? id : null);
        }
        finally
        {
            lock (_gate)
                if (ReferenceEquals(_pending, request))
                    _pending = null;
        }
    }

    private static List<ChatMessage> BuildMessages(CompanionDefinition pet, ChatConversation saved, string prompt)
    {
        static string Limit(string text, int max) => text.Length <= max ? text : text[..max];
        var examples = string.Join("\n", pet.GetDialogueExamples().Take(6)
            .Select(line => Limit(line.Replace("{{user}}", pet.UserNickname, StringComparison.OrdinalIgnoreCase), 160)));
        var styleGuide = examples.Length == 0 ? string.Empty
            : $"Use these lines as examples of style. Answer in your own words:\n{examples}\n";
        var notes = saved.Notes.TakeLast(12).Select(note => Limit(note, 160));
        var instruction = $"You are {Limit(pet.Name, 100)}, the user\u0027s desktop companion. Answer their latest message in character. " +
            "Write only your reply, in one or two short sentences. Be kind, especially when the user is upset. " +
            $"Description: {Limit(pet.Description, 600)}\nPersonality: {Limit(pet.Personality, 600)}\n" +
            $"Address the user as: {Limit(pet.UserNickname, 100)}\n" + styleGuide +
            $"Your saved notes about this user:\n{string.Join("\n", notes)}";
        if (CompanionVoice.LanguageTag(pet.Voice?.ReplyLanguage) is { Length: > 0 } language)
            instruction += $"\nWrite your reply in {System.Globalization.CultureInfo.GetCultureInfo(language).EnglishName}.";
        var messages = new List<ChatMessage> { new("system", instruction) };
        foreach (var turn in saved.Turns.TakeLast(4))
        {
            messages.Add(new("user", Limit(turn.Prompt, 350)));
            messages.Add(new("assistant", Limit(turn.Reply, 350)));
        }
        messages.Add(new("user", prompt));
        return messages;
    }
}
