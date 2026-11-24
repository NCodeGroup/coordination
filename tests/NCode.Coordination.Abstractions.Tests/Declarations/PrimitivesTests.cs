// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Coordination.Tests;

public sealed class PrimitivesTests
{
    private static string[] Names { get; } =
    [
        "session",
        "membership",
        "lease",
        "semaphore",
        "election",
        "claim",
        "record",
        "record-watch",
        "transaction",
        "occurrence",
        "set",
        "counter",
        "sequence",
        "notification",
    ];

    #region All Tests

    [Fact]
    public void All_Always_HoldsEachBuiltInPrimitiveOnceInOrder()
    {
        Assert.Equal(Names, Primitives.All.Select(primitive => primitive.Name));
        Assert.Equal(Names.Length, Primitives.All.Distinct().Count());
    }

    [Fact]
    public void All_Always_HoldsThePrimitivesTheStaticPropertiesReturn()
    {
        Primitive[] expected =
        [
            Primitives.Session,
            Primitives.Membership,
            Primitives.Lease,
            Primitives.Semaphore,
            Primitives.Election,
            Primitives.Claim,
            Primitives.Record,
            Primitives.RecordWatch,
            Primitives.Transaction,
            Primitives.Occurrence,
            Primitives.Set,
            Primitives.Counter,
            Primitives.Sequence,
            Primitives.Notification,
        ];

        Assert.Equal<Primitive>(expected, [.. Primitives.All]);
    }

    #endregion
}
