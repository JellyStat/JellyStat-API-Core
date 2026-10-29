namespace Jellystat_API_Core.Services.Webhooks
{
    public static class WebhookEvents
    {
        public const string PlaybackStarted = "playback.started";
        public const string PlaybackEnded = "playback.ended";
        public const string MediaAdded = "media.added";
        /// <summary>Sent by the Test button only; webhooks can't subscribe to it.</summary>
        public const string Test = "webhook.test";

        public static readonly string[] All = [PlaybackStarted, PlaybackEnded, MediaAdded];
    }

    public static class WebhookTypes
    {
        /// <summary>application/json: the standard payload, or the webhook's own template.</summary>
        public const string Generic = "generic";
        /// <summary>Discord webhook: an embed per event (a template replaces it).</summary>
        public const string Discord = "discord";

        public static readonly string[] All = [Generic, Discord];
    }

    /// <summary>One occurrence of an event; Data is one of the Webhook*Data records below.</summary>
    public record WebhookEvent(string Event, string? ServerId, string? ServerName, object Data)
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record WebhookUser(string Id, string Name);

    public record WebhookPlaybackItem(string Id, string Name, string Type, string? SeriesId, string? SeriesName, string? SeasonId,
        int? SeasonNumber, int? EpisodeNumber, long RuntimeSeconds);

    /// <summary>playback.started and playback.ended; WatchedSeconds is only set when playback ends.</summary>
    public record WebhookPlaybackData(WebhookUser User, WebhookPlaybackItem Item, string? Client, string? Device, string? DeviceId,
        string? PlayMethod, long? WatchedSeconds);

    public record WebhookMediaItem(string Id, string Name, string Type, string LibraryId, string? ParentId);

    /// <summary>media.added: Items holds the first MaxItems of Count.</summary>
    public record WebhookMediaAddedData(int Count, bool Truncated, IReadOnlyList<WebhookMediaItem> Items)
    {
        public const int MaxItems = 50;
    }

    public record WebhookTestData(string Message);

    public record WebhookDeliveryResult(bool Success, int? StatusCode, string? Error, long ElapsedMs);

    public static class WebhookSamples
    {
        private static readonly WebhookPlaybackItem SampleEpisode = new("sample-item-id", "Pilot", "Episode", "sample-series-id", "Sample Series", "sample-season-id", 1, 1, 2700);

        /// <summary>A realistic payload for each event, for tests and the token reference in the UI.</summary>
        public static WebhookEvent For(string eventKey, string? serverId, string? serverName) => eventKey switch
        {
            WebhookEvents.PlaybackStarted => new(eventKey, serverId, serverName,
                new WebhookPlaybackData(new("sample-user-id", "Sample User"), SampleEpisode, "Jellyfin Web", "Firefox", "sample-device-id", "DirectPlay", null)),
            WebhookEvents.PlaybackEnded => new(eventKey, serverId, serverName,
                new WebhookPlaybackData(new("sample-user-id", "Sample User"), SampleEpisode, "Jellyfin Web", "Firefox", "sample-device-id", "DirectPlay", 1520)),
            WebhookEvents.MediaAdded => new(eventKey, serverId, serverName,
                new WebhookMediaAddedData(2, false, [
                    new("sample-movie-id", "Sample Movie", "Movie", "sample-library-id", null),
                    new("sample-episode-id", "Pilot", "Episode", "sample-library-id", "sample-season-id")
                ])),
            _ => new(WebhookEvents.Test, serverId, serverName, new WebhookTestData("Test notification from Jellystat"))
        };
    }
}
