// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.InProcess;

/// <summary>
/// The provider that keeps records, transactions, occurrences, sets, counters and sequences in this process's memory.
/// </summary>
internal class InProcessProvider : ICoordinationProvider
{
    /// <summary>
    /// Why the provider lists a primitive as unsupported.
    /// </summary>
    private const string UnsupportedReason =
        "The in-process provider does not implement this primitive.";

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

        var primitives = new List<PrimitiveSupport>();
        foreach (var primitive in Primitives.All)
        {
            if (
                primitive == Primitives.Record
                || primitive == Primitives.Occurrence
                || primitive == Primitives.Set
            )
            {
                primitives.Add(PrimitiveSupport.Supported(primitive, state));
            }
            else if (primitive == Primitives.Transaction || primitive == Primitives.Counter)
            {
                primitives.Add(PrimitiveSupport.Supported(primitive, atomicState));
            }
            else if (primitive == Primitives.Sequence)
            {
                primitives.Add(PrimitiveSupport.Supported(primitive, sequence));
            }
            else
            {
                primitives.Add(PrimitiveSupport.Unsupported(primitive, UnsupportedReason));
            }
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
        Records = new InProcessRecordService(records);
        Transactions = new InProcessTransactionService(records);
        Occurrences = new InProcessOccurrenceService(time);
        Sets = new InProcessSetService(time);
        Counters = new InProcessCounterService(time);
        Sequences = new InProcessSequenceService();
    }

    /// <summary>
    /// Gets the record service.
    /// </summary>
    private InProcessRecordService Records { get; }

    /// <summary>
    /// Gets the transaction service.
    /// </summary>
    private InProcessTransactionService Transactions { get; }

    /// <summary>
    /// Gets the occurrence service.
    /// </summary>
    private InProcessOccurrenceService Occurrences { get; }

    /// <summary>
    /// Gets the set service.
    /// </summary>
    private InProcessSetService Sets { get; }

    /// <summary>
    /// Gets the counter service.
    /// </summary>
    private InProcessCounterService Counters { get; }

    /// <summary>
    /// Gets the sequence service.
    /// </summary>
    private InProcessSequenceService Sequences { get; }

    /// <inheritdoc />
    public ProviderDeclaration Declaration => DeclarationOfAll;

    /// <inheritdoc />
    public object? GetPrimitiveService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(IRecordService))
        {
            return Records;
        }

        if (serviceType == typeof(ITransactionService))
        {
            return Transactions;
        }

        if (serviceType == typeof(IOccurrenceService))
        {
            return Occurrences;
        }

        if (serviceType == typeof(ISetService))
        {
            return Sets;
        }

        if (serviceType == typeof(ICounterService))
        {
            return Counters;
        }

        if (serviceType == typeof(ISequenceService))
        {
            return Sequences;
        }

        return null;
    }

    /// <inheritdoc />
    public ValueTask ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
