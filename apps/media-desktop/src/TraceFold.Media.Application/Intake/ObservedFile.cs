using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Intake;

public sealed record ObservedFile(
    string FullPath,
    string Filename,
    string Extension,
    FileFingerprint Fingerprint,
    DateTimeOffset FirstObservedAtUtc);
