using Autofac;
using Reaparr.Identity.Services;

namespace Reaparr.Identity;

public class IdentityModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // See the comment in DataModule: without ExternallyOwned, Autofac's root-scope Disposer
        // pins every context it creates for the process lifetime. The download-client and torrent
        // auth pre-processors resolve an auth context on every request.
        builder.RegisterType<AuthDbContext>().As<IAuthDbContext>().AsSelf().ExternallyOwned();

        builder.RegisterType<AuthDbContext>().As<IAuthDbContextDatabase>().ExternallyOwned();

        builder.RegisterType<AuthDbContextFactory>().As<IAuthDbContextFactory>().InstancePerDependency();

        builder.RegisterType<IdentityUserService>().As<IUserService>().InstancePerLifetimeScope();

        builder.RegisterType<IdentityRoleService>().As<IRoleService>().InstancePerLifetimeScope();

        builder.RegisterType<IdentityIdentitySignInService>().As<IIdentitySignInService>().InstancePerLifetimeScope();
    }
}
