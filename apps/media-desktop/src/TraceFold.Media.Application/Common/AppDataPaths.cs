namespace TraceFold.Media.Application.Common;

public sealed record AppDataPaths(
    string RootDirectory,
    string DatabasePath,
    string LogsDirectory,
    string DefaultWorkDirectory);
