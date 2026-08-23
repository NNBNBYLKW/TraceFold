namespace TraceFold.Media.Application.Processing;

public sealed class MediaProcessorRegistry : IMediaProcessorRegistry
{
    private readonly IReadOnlyDictionary<string, IMediaProcessor> processorsByCapability;

    public MediaProcessorRegistry(IEnumerable<IMediaProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        IMediaProcessor[] materialized = processors.ToArray();
        foreach (IMediaProcessor processor in materialized)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(processor.Descriptor.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(processor.Descriptor.Version);
            ArgumentException.ThrowIfNullOrWhiteSpace(processor.Descriptor.Capability);
        }

        if (materialized
            .GroupBy(processor => processor.Descriptor.Key, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Media processor keys must be unique.");
        }

        var selected = new Dictionary<string, IMediaProcessor>(StringComparer.Ordinal);
        foreach (IGrouping<string, IMediaProcessor> capabilityGroup in materialized
                     .GroupBy(
                         processor => processor.Descriptor.Capability,
                         StringComparer.Ordinal))
        {
            IMediaProcessor[] ordered = capabilityGroup
                .OrderByDescending(processor => processor.Descriptor.Priority)
                .ToArray();
            if (ordered.Length > 1
                && ordered[0].Descriptor.Priority == ordered[1].Descriptor.Priority)
            {
                throw new InvalidOperationException(
                    "The highest processor priority for a capability must be unique.");
            }

            selected.Add(capabilityGroup.Key, ordered[0]);
        }

        processorsByCapability = selected;
    }

    public IMediaProcessor Resolve(string capability)
    {
        if (capability is not null
            && processorsByCapability.TryGetValue(capability, out IMediaProcessor? processor))
        {
            return processor;
        }

        throw new ProcessingCapabilityUnavailableException();
    }
}
