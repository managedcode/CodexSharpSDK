using ManagedCode.CodexSharp.Internal;

namespace ManagedCode.CodexSharp;

public sealed record Usage(int InputTokens, int CachedInputTokens, int OutputTokens);

public sealed record ThreadError(string Message);

public abstract record ThreadEvent(string Type);

public sealed record ThreadStartedEvent(string ThreadId)
    : ThreadEvent(CodexProtocolConstants.EventTypes.ThreadStarted);

public sealed record TurnStartedEvent()
    : ThreadEvent(CodexProtocolConstants.EventTypes.TurnStarted);

public sealed record TurnCompletedEvent(Usage Usage)
    : ThreadEvent(CodexProtocolConstants.EventTypes.TurnCompleted);

public sealed record TurnFailedEvent(ThreadError Error)
    : ThreadEvent(CodexProtocolConstants.EventTypes.TurnFailed);

public sealed record ItemStartedEvent(ThreadItem Item)
    : ThreadEvent(CodexProtocolConstants.EventTypes.ItemStarted);

public sealed record ItemUpdatedEvent(ThreadItem Item)
    : ThreadEvent(CodexProtocolConstants.EventTypes.ItemUpdated);

public sealed record ItemCompletedEvent(ThreadItem Item)
    : ThreadEvent(CodexProtocolConstants.EventTypes.ItemCompleted);

public sealed record ThreadErrorEvent(string Message)
    : ThreadEvent(CodexProtocolConstants.EventTypes.Error);
