namespace TraceFold.Media.Desktop.Services;

public interface IMetadataEditorFlushable
{
    Task FlushAsync(CancellationToken cancellationToken);
}

public interface IMetadataEditorRegistry
{
    void Register(IMetadataEditorFlushable editor);

    Task FlushAllAsync(CancellationToken cancellationToken);
}

public sealed class MetadataEditorRegistry : IMetadataEditorRegistry
{
    private readonly object _sync = new();
    private readonly List<WeakReference<IMetadataEditorFlushable>> _editors = [];

    public void Register(IMetadataEditorFlushable editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        lock (_sync)
        {
            _editors.RemoveAll(reference => !reference.TryGetTarget(out _));
            if (_editors.Any(reference =>
                reference.TryGetTarget(out IMetadataEditorFlushable? target)
                && ReferenceEquals(target, editor)))
            {
                return;
            }

            _editors.Add(new WeakReference<IMetadataEditorFlushable>(editor));
        }
    }

    public async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        IMetadataEditorFlushable[] editors;
        lock (_sync)
        {
            var alive = new List<IMetadataEditorFlushable>(_editors.Count);
            _editors.RemoveAll(reference =>
            {
                if (!reference.TryGetTarget(out IMetadataEditorFlushable? target))
                {
                    return true;
                }

                alive.Add(target);
                return false;
            });
            editors = [.. alive.Distinct()];
        }

        var errors = new List<Exception>();
        foreach (IMetadataEditorFlushable editor in editors)
        {
            try
            {
                await editor.FlushAsync(cancellationToken);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (errors.Count > 0)
        {
            throw new AggregateException(errors);
        }
    }
}
