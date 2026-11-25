// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Coordination.Composition;
using NCode.Coordination.Telemetry;

namespace NCode.Coordination.Routing;

/// <summary>
/// Routes each call to the provider bound for its key's keyspace, and traces and measures it.
/// </summary>
/// <param name="composition">Resolves the provider bound for a keyspace.</param>
/// <param name="instrumentation">Traces and measures each call.</param>
internal class DefaultTransactionService(
    CoordinationComposition composition,
    OperationInstrumentation instrumentation
) : ITransactionService
{
    /// <summary>
    /// Gets the composition that resolves the provider bound for a key's keyspace.
    /// </summary>
    private CoordinationComposition Composition { get; } = composition;

    /// <summary>
    /// Gets the instrumentation that traces and measures each call.
    /// </summary>
    private OperationInstrumentation Instrumentation { get; } = instrumentation;

    /// <inheritdoc />
    public ValueTask<TransactionResult> ExecuteAsync(
        RecordTransaction transaction,
        OperationOptions? options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var target = Composition.Resolve<ITransactionService>(
            Primitives.Transaction,
            transaction.Keyspace
        );
        return Instrumentation.MeasureAsync(target, Operations.TransactionExecute, service =>
            service.ExecuteAsync(transaction, options, cancellationToken)
        );
    }
}
