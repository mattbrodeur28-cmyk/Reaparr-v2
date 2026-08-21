using Autofac;
using Autofac.Core;

namespace Reaparr.Data.UnitTests;

/// <summary>
/// Guards the ownership of the DbContext registrations.
/// </summary>
/// <remarks>
/// Autofac's Disposer keeps a strong reference to every IDisposable it creates until the owning
/// scope is disposed. FastEndpoints resolves command handlers from the root provider - there is no
/// ExecuteAsync overload that accepts a scope - and 70 handlers take IReaparrDbContext by
/// constructor, so with scope-owned registrations every command execution pinned one DbContext for
/// the lifetime of the process. That is what drove the container to roughly 20GB.
///
/// <para>
/// This asserts the registration metadata rather than trying to observe the leak at runtime. An
/// earlier attempt to test it behaviourally was worthless: counting created-minus-disposed passes
/// either way (call sites do dispose), and the integration test harness substitutes its own
/// DbContext registration, so it never exercised this module at all. Checking the invariant
/// directly is both cheaper and impossible to satisfy accidentally.
/// </para>
/// </remarks>
public class DataModuleRegistrationUnitTests
{
    [Test]
    public void ShouldRegisterDbContextAsExternallyOwned_WhenModuleIsLoaded()
    {
        // Arrange
        var builder = new ContainerBuilder();
        builder.RegisterModule<DataModule>();

        // Act
        using var container = builder.Build();

        var dbContextRegistrations = container
            .ComponentRegistry.Registrations.Where(x =>
                x.Activator.LimitType == typeof(ReaparrDbContext)
            )
            .ToList();

        // Assert
        dbContextRegistrations.ShouldNotBeEmpty("DataModule should register ReaparrDbContext");

        foreach (var registration in dbContextRegistrations)
        {
            registration.Ownership.ShouldBe(
                InstanceOwnership.ExternallyOwned,
                "ReaparrDbContext must be ExternallyOwned. Scope-owned registrations are retained by "
                    + "Autofac's root Disposer for the entire process, which leaks one DbContext per "
                    + "command execution."
            );
        }
    }
}
