// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The provider that keeps every primitive in this process's memory.
/// </summary>
internal class InProcessProvider : ICoordinationProvider, IDisposable
{
    /// <summary>
    /// Gets what every in-process provider declares.
    /// </summary>
    private static ProviderDeclaration DeclarationOfAll { get; } = CreateDeclaration();

    /// <summary>
    /// Creates the declaration, which lists the primitives in the order of <see cref="Primitives.All"/>.
    /// </summary>
    /// <returns>The declaration.</returns>
    private static ProviderDeclaration CreateDeclaration()
    {
        Guarantee[] state =
        [
            Guarantees.LinearizableWithinProcess,
            Guarantees.Volatile,
            Guarantees.ServerClock,
        ];

        Guarantee[] atomicState =
        [
            Guarantees.LinearizableWithinProcess,
            Guarantees.Volatile,
            Guarantees.ServerClock,
            Guarantees.AtomicAcrossPartitions,
        ];

        Guarantee[] sequence = [Guarantees.LinearizableWithinProcess, Guarantees.Volatile];

        Guarantee[] watch =
        [
            Guarantees.LinearizableWithinProcess,
            Guarantees.AtMostOnceWithGapDetection,
        ];

        Guarantee[] notification = [Guarantees.AtMostOnceWithGapDetection];

        var primitives = new List<PrimitiveSupport>();
        foreach (var primitive in Primitives.All)
        {
            Guarantee[] guarantees;
            if (primitive == Primitives.RecordWatch)
            {
                guarantees = watch;
            }
            else if (primitive == Primitives.Notification)
            {
                guarantees = notification;
            }
            else if (primitive == Primitives.Transaction || primitive == Primitives.Counter)
            {
                guarantees = atomicState;
            }
            else if (primitive == Primitives.Sequence)
            {
                guarantees = sequence;
            }
            else
            {
                guarantees = state;
            }

            var support = PrimitiveSupport.Supported(primitive, guarantees);
            primitives.Add(support);
        }

        return new ProviderDeclaration(
            InProcessCoordinationBuilderExtensions.ProviderName,
            primitives
        );
    }

    /// <summary>
    /// Creates the provider and its state tables.
    /// </summary>
    /// <param name="time">The clock that the provider's expiries are measured on.</param>
    /// <exception cref="ArgumentNullException">The clock is null.</exception>
    public InProcessProvider(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        var records = new InProcessRecordTable(time);
        var sessions = new InProcessSessionTable(records);
        RecordTable = records;
        Services = new Dictionary<Type, object>
        {
            [typeof(ISessionService)] = new InProcessSessionService(sessions),
            [typeof(IMembershipService)] = new InProcessMembershipService(sessions),
            [typeof(ILeaseService)] = new InProcessLeaseService(sessions),
            [typeof(ISemaphoreService)] = new InProcessSemaphoreService(sessions),
            [typeof(IElectionService)] = new InProcessElectionService(sessions),
            [typeof(IClaimService)] = new InProcessClaimService(time),
            [typeof(IRecordService)] = new InProcessRecordService(sessions),
            [typeof(ITransactionService)] = new InProcessTransactionService(sessions),
            [typeof(IRecordWatchService)] = new InProcessRecordWatchService(records),
            [typeof(IOccurrenceService)] = new InProcessOccurrenceService(time),
            [typeof(ISetService)] = new InProcessSetService(time),
            [typeof(ICounterService)] = new InProcessCounterService(time),
            [typeof(ISequenceService)] = new InProcessSequenceService(),
            [typeof(INotificationService)] = new InProcessNotificationService(),
        };
    }

    /// <summary>
    /// Gets the record table, whose deadlines the provider stops when it is disposed.
    /// </summary>
    private InProcessRecordTable RecordTable { get; }

    /// <summary>
    /// Gets the primitive services by the interface that each implements.
    /// </summary>
    private Dictionary<Type, object> Services { get; }

    /// <inheritdoc />
    public ProviderDeclaration Declaration => DeclarationOfAll;

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return Services.GetValueOrDefault(serviceType);
    }

    /// <inheritdoc />
    public ValueTask ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stops the timer that runs the provider's deadlines.
    /// </summary>
    public void Dispose()
    {
        RecordTable.Dispose();
    }
}
