namespace TraceFold.Media.Infrastructure.MediaTools;

public sealed record MediaToolOptions(string RootDirectory);

public sealed record MediaToolLocation(
    string FfmpegPath,
    string FfprobePath);
