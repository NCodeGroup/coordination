// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using StackExchange.Redis;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// The seam between the provider and its server: every call to the server goes through it, and every failure of the
/// client leaves it as a <see cref="CoordinationException"/>.
/// </summary>
internal sealed class RespServer
{
    /// <summary>
    /// Gets the connection of the provider to its server.
    /// </summary>
    private RespConnection Connection { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RespServer"/> class.
    /// </summary>
    /// <param name="connection">The connection of the provider to its server.</param>
    /// <exception cref="ArgumentNullException">The connection is null.</exception>
    public RespServer(RespConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Connection = connection;
    }

    /// <summary>
    /// Runs a script on the server.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="keys">The keys that the script reads or writes.</param>
    /// <param name="arguments">The arguments of the script.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the reply.</param>
    /// <returns>The result of the script.</returns>
    /// <exception cref="ArgumentNullException">The script, the keys or the arguments are null.</exception>
    /// <exception cref="CoordinationException">The call failed; the exception says whether it may have taken effect.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public async ValueTask<RedisResult> EvaluateAsync(
        RespScript script,
        RedisKey[] keys,
        RedisValue[] arguments,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(arguments);

        var multiplexer = await Connection.GetAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var database = multiplexer.GetDatabase();

            return await database
                .ScriptEvaluateAsync(script.Text, keys, arguments)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (RespFailures.IsClientFailure(e))
        {
            throw RespFailures.Translate(e, isScript: true);
        }
    }

    /// <summary>
    /// Runs a single command on the server.
    /// </summary>
    /// <param name="command">The name of the command.</param>
    /// <param name="arguments">The arguments of the command.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the reply.</param>
    /// <returns>The reply of the server.</returns>
    /// <exception cref="ArgumentNullException">The command or the arguments are null.</exception>
    /// <exception cref="ArgumentException">The command is empty or white space.</exception>
    /// <exception cref="CoordinationException">The call failed; the exception says whether it may have taken effect.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public async ValueTask<RedisResult> ExecuteAsync(
        string command,
        object[] arguments,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);

        var multiplexer = await Connection.GetAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var database = multiplexer.GetDatabase();

            return await database
                .ExecuteAsync(command, arguments)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (RespFailures.IsClientFailure(e))
        {
            throw RespFailures.Translate(e, isScript: false);
        }
    }

    /// <summary>
    /// Reads a section of the <c>INFO</c> reply of the server.
    /// </summary>
    /// <param name="section">The name of the section, such as <c>server</c>.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the reply.</param>
    /// <returns>The <c>field:value</c> pairs of the section, by field name.</returns>
    /// <exception cref="ArgumentNullException">The section is null.</exception>
    /// <exception cref="ArgumentException">The section is empty or white space.</exception>
    /// <exception cref="CoordinationException">The call failed; the exception says whether it may have taken effect.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public async ValueTask<IReadOnlyDictionary<string, string>> InfoAsync(
        string section,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var result = await ExecuteAsync("INFO", [section], cancellationToken).ConfigureAwait(false);
        var text = result.ToString() ?? string.Empty;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            fields[line[..separator]] = line[(separator + 1)..];
        }

        return fields;
    }
}
