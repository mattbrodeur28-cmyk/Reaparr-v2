namespace Reaparr.AppHost;

/// <summary>
///  Autofac module for the AppHost project.
/// </summary>
public class AppHostModule : Module
{
    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        // ExternallyOwned: disposing a client from IHttpClientFactory is a documented no-op (the
        // handler is pooled), so Autofac tracking it as a disposable is pure leak - every resolve
        // from the root scope pinned one forever.
        builder.Register(c => c.Resolve<IHttpClientFactory>().CreateClient()).As<HttpClient>().ExternallyOwned();

        // This needs to be registered in order to fire Boot on Application startup
        builder.RegisterType<Boot>().As<IHostedService>().SingleInstance();

        builder
            .RegisterType<DesktopSingleInstanceCoordinator>()
            .As<IDesktopSingleInstanceCoordinator>()
            .SingleInstance();
        builder.RegisterType<DesktopWindow>().As<IDesktopWindow>().InstancePerDependency();
        builder.RegisterType<DesktopMode>().As<IDesktopMode>().SingleInstance();
    }
}
