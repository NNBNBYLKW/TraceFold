namespace TraceFold.Media.Domain.Ingest;

[Flags]
public enum ConflictReason
{
    None = 0,
    Name = 1,
    Content = 2
}
