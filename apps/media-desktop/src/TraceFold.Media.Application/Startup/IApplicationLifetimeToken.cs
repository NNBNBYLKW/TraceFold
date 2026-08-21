namespace TraceFold.Media.Application.Startup;

public interface IApplicationLifetimeToken
{
    CancellationToken ApplicationStopping { get; }
}
