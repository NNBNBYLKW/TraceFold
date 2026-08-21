namespace TraceFold.Media.Application.Intake;

public enum CandidateDisposition
{
    WaitingForCompanion,
    Direct,
    RequiresProcessing
}

public sealed record CandidateGroup(
    string RuleName,
    string GroupKey,
    MediaKind MediaKind,
    CandidateDisposition Disposition,
    IReadOnlyList<ObservedFile> Sources,
    string? RequiredCapability);
