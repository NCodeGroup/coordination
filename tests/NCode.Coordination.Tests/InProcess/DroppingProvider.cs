// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess.Tests;

/// <summary>
/// A provider whose connection a test drops: it decorates the records and notifications of another provider and
/// serves the rest as they are.
/// </summary>
/// <param name="inner">The provider to decorate.</param>
internal sealed class DroppingProvider(ICoordinationProvider inner)
    : ICoordinationProvider,
        IDisposable
{
    /// <summary>
    /// Gets the connection that a test drops.
    /// </summary>
    public DroppingConnection Connection { get; } = new();

    /// <summary>
    /// Gets the provider that this one decorates.
    /// </summary>
    private ICoordinationProvider Inner { get; } = inner;

    /// <inheritdoc />
    public ProviderDeclaration Declaration => Inner.Declaration;

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var service = Inner.GetPrimitiveService(serviceType);

        if (serviceType == typeof(IRecordService) && service is IRecordService records)
        {
            return new DroppingRecordService(records, Connection);
        }

        if (
            serviceType == typeof(INotificationService)
            && service is INotificationService notifications
        )
        {
            return new DroppingNotificationService(notifications, Connection);
        }

        return service;
    }

    /// <inheritdoc />
    public ValueTask ProbeAsync(CancellationToken cancellationToken) =>
        Inner.ProbeAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
