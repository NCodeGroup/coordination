// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Npgsql;

namespace NCode.Coordination.PostgreSql.Server;

/// <summary>
/// Reads the results of one batch in order, which are those of its statements that return rows.
/// </summary>
/// <param name="reader">The reader of the batch.</param>
internal sealed class PgResults(NpgsqlDataReader reader)
{
    /// <summary>
    /// Reads nothing from the results of a batch that holds no result to read.
    /// </summary>
    /// <param name="results">The results of the batch.</param>
    /// <param name="cancellationToken">The token is unused because the batch holds no result to read.</param>
    /// <returns>True, as the batch has been sent.</returns>
    public static ValueTask<bool> IgnoreAsync(
        PgResults results,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// Gets the reader of the batch.
    /// </summary>
    private NpgsqlDataReader Reader { get; } = reader;

    /// <summary>
    /// Gets or sets whether the current result of the reader was handed out.
    /// </summary>
    private bool Started { get; set; }

    /// <summary>
    /// Reads every row of the next result: the current one on the first call, then each after it.
    /// </summary>
    /// <typeparam name="T">The type of a mapped row.</typeparam>
    /// <param name="map">Maps the reader's current row.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The mapped rows, in the order the server sent them.</returns>
    /// <exception cref="InvalidOperationException">The batch has no further result.</exception>
    public async ValueTask<ImmutableArray<T>> ReadAsync<T>(
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(map);

        if (!await MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The batch has no further result.");
        }

        var rows = ImmutableArray.CreateBuilder<T>();
        while (await Reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = map(Reader);
            rows.Add(row);
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// Moves past the next result, whose rows the caller doesn't use.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>A task that completes when the result's rows have been read.</returns>
    /// <exception cref="InvalidOperationException">The batch has no further result.</exception>
    public async ValueTask SkipAsync(CancellationToken cancellationToken)
    {
        if (!await MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The batch has no further result.");
        }

        while (await Reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }
    }

    /// <summary>
    /// Reads every remaining result, so that an error later in the batch surfaces.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>A task that completes when the batch has been read to its end.</returns>
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        while (await MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await Reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }
        }
    }

    /// <summary>
    /// Moves to the next result: the current one on the first call, then each after it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>True when a result is current; otherwise, false.</returns>
    private async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        if (!Started)
        {
            Started = true;
            return true;
        }

        return await Reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
    }
}
