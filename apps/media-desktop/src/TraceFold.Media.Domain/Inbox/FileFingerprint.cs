namespace TraceFold.Media.Domain.Inbox;

public sealed record FileFingerprint(long Size, DateTimeOffset LastWriteTimeUtc);
