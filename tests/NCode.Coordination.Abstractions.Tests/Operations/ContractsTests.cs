// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class ContractsTests
{
    private static (Primitive Primitive, Type Contract)[] Table { get; } =
    [
        (Primitives.Session, typeof(ISessionService)),
        (Primitives.Membership, typeof(IMembershipService)),
        (Primitives.Lease, typeof(ILeaseService)),
        (Primitives.Semaphore, typeof(ISemaphoreService)),
        (Primitives.Election, typeof(IElectionService)),
        (Primitives.Claim, typeof(IClaimService)),
        (Primitives.Record, typeof(IRecordService)),
        (Primitives.RecordWatch, typeof(IRecordWatchService)),
        (Primitives.Transaction, typeof(ITransactionService)),
        (Primitives.Occurrence, typeof(IOccurrenceService)),
        (Primitives.Set, typeof(ISetService)),
        (Primitives.Counter, typeof(ICounterService)),
        (Primitives.Sequence, typeof(ISequenceService)),
        (Primitives.Notification, typeof(INotificationService)),
    ];

    private static bool IsAllowedReturnType(Type type)
    {
        if (type == typeof(ValueTask))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition == typeof(ValueTask<>) || definition == typeof(IAsyncEnumerable<>);
    }

    #region Table Tests

    [Fact]
    public void Table_Always_CoversEveryPrimitiveOnce()
    {
        Primitive[] actual = [.. Table.Select(row => row.Primitive)];

        Assert.Equal<Primitive>(Primitives.All, actual);
    }

    #endregion

    #region Contract Tests

    [Fact]
    public void Contracts_Always_HaveOneMethodPerOperation()
    {
        foreach (var (primitive, contract) in Table)
        {
            var operations = Operations.Definitions.Count(d => d.Primitive == primitive);

            Assert.Equal(operations, contract.GetMethods().Length);
        }
    }

    [Fact]
    public void Contracts_Always_EndWithOptionsAndCancellationToken()
    {
        foreach (var method in Table.SelectMany(row => row.Contract.GetMethods()))
        {
            var parameters = method.GetParameters();

            Assert.True(parameters.Length >= 2, method.Name);
            Assert.Equal(typeof(OperationOptions), parameters[^2].ParameterType);
            Assert.Equal(typeof(CancellationToken), parameters[^1].ParameterType);
            Assert.True(IsAllowedReturnType(method.ReturnType), method.Name);
        }
    }

    #endregion
}
