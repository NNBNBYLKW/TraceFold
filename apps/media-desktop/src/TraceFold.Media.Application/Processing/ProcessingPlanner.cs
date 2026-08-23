using TraceFold.Media.Domain.Inbox;

namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingPlanner(IMediaProcessorRegistry registry) : IProcessingPlanner
{
    public PlannedProcessing Create(ProcessingCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.State != InboxItemState.RequiresProcessing
            || string.IsNullOrWhiteSpace(candidate.RequiredCapability)
            || candidate.Sources.Count == 0
            || candidate.PreviousAttempts < 0
            || candidate.Sources.Any(source => !IsSha256(source.Sha256)))
        {
            throw Reject();
        }

        IMediaProcessor processor = registry.Resolve(candidate.RequiredCapability);
        Guid jobId = Guid.NewGuid();
        ProcessingPlanDefinition plan = processor.CreatePlan(jobId, candidate);
        ValidatePlan(plan, processor.Descriptor, jobId, candidate);
        return new PlannedProcessing(jobId, checked(candidate.PreviousAttempts + 1), plan);
    }

    private static void ValidatePlan(
        ProcessingPlanDefinition plan,
        MediaProcessorDescriptor descriptor,
        Guid jobId,
        ProcessingCandidate candidate)
    {
        string expectedWorkDirectory = Path.Combine(
            ".tracefold",
            "processing",
            jobId.ToString("N"));
        if (plan.Id == Guid.Empty
            || plan.JobId != jobId
            || plan.InboxItemId != candidate.InboxItemId
            || !string.Equals(plan.ProcessorKey, descriptor.Key, StringComparison.Ordinal)
            || !string.Equals(plan.ProcessorVersion, descriptor.Version, StringComparison.Ordinal)
            || !string.Equals(
                plan.ProcessorDisplayName,
                descriptor.DisplayName,
                StringComparison.Ordinal)
            || !string.Equals(plan.Capability, descriptor.Capability, StringComparison.Ordinal)
            || !string.Equals(
                plan.WorkDirectoryRelativePath,
                expectedWorkDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Reject();
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character =>
            character is >= '0' and <= '9'
            or >= 'A' and <= 'F'
            or >= 'a' and <= 'f');

    private static ProcessingPlanRejectedException Reject() =>
        new("processing.plan_rejected");
}
