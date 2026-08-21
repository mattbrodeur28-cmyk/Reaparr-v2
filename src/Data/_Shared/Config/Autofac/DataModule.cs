using Autofac;

namespace Reaparr.Data;

public class DataModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // ExternallyOwned is load-bearing, not cosmetic. Autofac's Disposer holds a strong
        // reference to every IDisposable it creates until the OWNING SCOPE is disposed - calling
        // Dispose() on the instance does not remove it. FastEndpoints resolves command handlers
        // from the root provider (no ExecuteAsync overload accepts a scope), and 70 handlers take
        // IReaparrDbContext by constructor, so without this every command execution roots one
        // DbContext in the root scope for the lifetime of the process. That was ~20GB of RSS.
        //
        // Safe because the production constructor (ReaparrDbContext.cs, the 2-arg overload) does
        // not open a connection eagerly, connection pooling is on, and 32 of 35 factory sites
        // already use `using`. Do NOT "fix" this by switching to InstancePerLifetimeScope: from
        // the root scope that yields one process-wide DbContext shared across threads.
        builder.RegisterType<ReaparrDbContext>().As<IReaparrDbContext>().AsSelf().ExternallyOwned();

        builder.RegisterType<ReaparrDbContext>().As<IReaparrDbContextDatabase>().ExternallyOwned();

        builder.RegisterType<ReaparrDbContextManager>().As<IReaparrDbContextManager>().InstancePerDependency();

        builder.RegisterType<ReaparrDbContextFactory>().As<IReaparrDbContextFactory>().InstancePerDependency();

        builder.RegisterType<MediaQueryCache>().As<IMediaQueryCache>().SingleInstance();
    }
}
