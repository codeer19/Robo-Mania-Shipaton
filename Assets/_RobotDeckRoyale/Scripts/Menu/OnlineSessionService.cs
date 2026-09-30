using System;

public enum OnlineSessionRequest
{
    QuickMatch,
    CreateRoom,
    JoinRoom
}

public readonly struct OnlineSessionResult
{
    public bool Success { get; }
    public string Message { get; }
    public string RoomCode { get; }

    public OnlineSessionResult(bool success, string message, string roomCode = "")
    {
        Success = success;
        Message = message ?? string.Empty;
        RoomCode = roomCode ?? string.Empty;
    }
}

/// <summary>
/// Adapter contract for Lobby/Relay, Photon, GameLift or another real multiplayer provider.
/// Providers own authentication, room creation and scene handoff and must only report success
/// after the backend operation has completed.
/// </summary>
public interface IOnlineSessionProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    string StatusMessage { get; }
    bool IsBusy { get; }

    void QuickMatch(Action<OnlineSessionResult> completed);
    void CreateRoom(Action<OnlineSessionResult> completed);
    void JoinRoom(string roomCode, Action<OnlineSessionResult> completed);
}

public static class OnlineSessionService
{
    private const string MissingProviderMessage =
        "ONLINE PLAY IS NOT CONFIGURED YET. CONNECT A LOBBY/RELAY OR OTHER SESSION PROVIDER.";

    private static IOnlineSessionProvider provider;

    public static IOnlineSessionProvider Provider => provider;
    public static bool IsConfigured => provider != null && provider.IsConfigured;
    public static bool IsBusy => provider != null && provider.IsBusy;
    public static string StatusMessage => provider == null ? MissingProviderMessage : provider.StatusMessage;

    public static void RegisterProvider(IOnlineSessionProvider sessionProvider)
    {
        provider = sessionProvider;
    }

    public static void UnregisterProvider(IOnlineSessionProvider sessionProvider)
    {
        if (ReferenceEquals(provider, sessionProvider))
        {
            provider = null;
        }
    }

    public static void QuickMatch(Action<OnlineSessionResult> completed)
    {
        if (!CanStart(completed))
        {
            return;
        }

        provider.QuickMatch(completed);
    }

    public static void CreateRoom(Action<OnlineSessionResult> completed)
    {
        if (!CanStart(completed))
        {
            return;
        }

        provider.CreateRoom(completed);
    }

    public static void JoinRoom(string roomCode, Action<OnlineSessionResult> completed)
    {
        if (!CanStart(completed))
        {
            return;
        }

        string cleaned = roomCode == null ? string.Empty : roomCode.Trim().ToUpperInvariant();
        if (cleaned.Length < 4)
        {
            completed?.Invoke(new OnlineSessionResult(false, "ENTER A VALID ROOM CODE"));
            return;
        }

        provider.JoinRoom(cleaned, completed);
    }

    private static bool CanStart(Action<OnlineSessionResult> completed)
    {
        if (!IsConfigured)
        {
            completed?.Invoke(new OnlineSessionResult(false, StatusMessage));
            return false;
        }

        if (provider.IsBusy)
        {
            completed?.Invoke(new OnlineSessionResult(false, "AN ONLINE REQUEST IS ALREADY IN PROGRESS"));
            return false;
        }

        return true;
    }
}
