using Autofac;

namespace Reaparr.PlexApi;

public class PlexApiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // IPlexApiClient is IDisposable, so see the comment in DataModule: without ExternallyOwned
        // every client resolved from the root scope is pinned for the process lifetime.
        builder.RegisterType<PlexApiClient>().As<IPlexApiClient>().ExternallyOwned();

        builder.RegisterType<PlexApiClientFactory>().As<IPlexApiClientFactory>().InstancePerLifetimeScope();
    }
}
