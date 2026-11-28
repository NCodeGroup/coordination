// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using NCode.Coordination.PostgreSql.Server;

namespace NCode.Coordination.PostgreSql.Tests;

public sealed class PgProviderFactoryTests
{
    #region Create Tests

    [Fact]
    public async Task Create_NeitherSource_Throws()
    {
        var factory = new PgProviderFactory(
            null,
            null,
            PostgreSqlProfiles.Asynchronous,
            PgSettings.Default
        );
        var collection = new ServiceCollection();
        await using var services = collection.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create(services));

        Assert.Equal(PgProviderFactory.NoSourceMessage, exception.Message);
    }

    #endregion
}
