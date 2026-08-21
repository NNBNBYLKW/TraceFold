using Microsoft.Extensions.Hosting;
using TraceFold.Media.Application.Startup;

namespace TraceFold.Media.Infrastructure.Workers;

public sealed class HostApplicationLifetimeToken(
    IHostApplicationLifetime lifetime) : IApplicationLifetimeToken
{
    public CancellationToken ApplicationStopping => lifetime.ApplicationStopping;
}
