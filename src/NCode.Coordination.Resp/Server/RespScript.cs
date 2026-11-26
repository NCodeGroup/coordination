// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// A Lua script that the provider sends to the server, loaded from the assembly's embedded resources.
/// </summary>
/// <param name="name">The name of the script.</param>
/// <param name="text">The Lua source of the script.</param>
/// <exception cref="ArgumentNullException">The name or the text is null.</exception>
/// <exception cref="ArgumentException">The name or the text is empty or white space.</exception>
internal sealed class RespScript(string name, string text)
{
    /// <summary>The start of a script's manifest resource name.</summary>
    private const string ResourcePrefix = "NCode.Coordination.Resp.Scripts.";

    /// <summary>The end of a script's manifest resource name.</summary>
    private const string ResourceSuffix = ".lua";

    /// <summary>
    /// The name of the start that every record script shares.
    /// </summary>
    private const string RecordPrelude = "record-prelude";

    /// <summary>
    /// Gets the startup probe: it proves the server runs Lua and lets the user run scripts,
    /// and returns the server section of INFO.
    /// </summary>
    public static RespScript Probe { get; } = Load("probe");

    /// <summary>
    /// Gets the startup probe's permission check: it returns the commands among its arguments
    /// that the server's user may not run, without running any of them.
    /// </summary>
    public static RespScript ProbeCommands { get; } = Load("probe-commands");

    /// <summary>
    /// Gets the counter script: it applies the increments of a batch in order, and stops after the first
    /// whose count exceeds its limit.
    /// </summary>
    public static RespScript CounterIncrement { get; } = Load("counter-increment");

    /// <summary>
    /// Gets the set-add script: it adds a member with its deadline, and refuses before writing when the set is full.
    /// </summary>
    public static RespScript SetAdd { get; } = Load("set-add");

    /// <summary>
    /// Gets the set-remove script: it removes a member, and reports whether the member was live.
    /// </summary>
    public static RespScript SetRemove { get; } = Load("set-remove");

    /// <summary>
    /// Gets the set-list script: it lists the members whose deadline is after the server's current time.
    /// </summary>
    public static RespScript SetList { get; } = Load("set-list");

    /// <summary>
    /// Gets the record-create script: it creates a record when its key is absent.
    /// </summary>
    public static RespScript RecordCreate { get; } = LoadRecord("record-create");

    /// <summary>
    /// Gets the record-read script: it reads a live record.
    /// </summary>
    public static RespScript RecordRead { get; } = LoadRecord("record-read");

    /// <summary>
    /// Gets the record-swap script: it replaces a record's value when its revision matches.
    /// </summary>
    public static RespScript RecordSwap { get; } = LoadRecord("record-swap");

    /// <summary>
    /// Gets the record-delete script: it deletes a record when its revision matches.
    /// </summary>
    public static RespScript RecordDelete { get; } = LoadRecord("record-delete");

    /// <summary>
    /// Gets the record-take script: it reads a record and deletes it.
    /// </summary>
    public static RespScript RecordTake { get; } = LoadRecord("record-take");

    /// <summary>
    /// Gets the record-list script: it lists a page of the live records under a prefix.
    /// </summary>
    public static RespScript RecordList { get; } = LoadRecord("record-list");

    /// <summary>
    /// Gets the record-transaction script: it checks conditions and runs one of two branches of writes.
    /// </summary>
    public static RespScript RecordTransaction { get; } = LoadRecord("record-transaction");

    /// <summary>
    /// Gets the session script: it manages session creation, keep-alive, closing, and liveness checks.
    /// </summary>
    public static RespScript Session { get; } = Load("sessions");

    /// <summary>
    /// Gets the membership script: it manages group membership with join, leave, and listing operations.
    /// </summary>
    public static RespScript Membership { get; } = Load("membership");

    /// <summary>
    /// Gets the lease script: it manages lease acquisition, renewal, and release operations.
    /// </summary>
    public static RespScript Lease { get; } = Load("leases");

    /// <summary>
    /// Gets the semaphore script: it manages semaphore slot acquisition, renewal, and release operations.
    /// </summary>
    public static RespScript Semaphore { get; } = Load("semaphores");

    /// <summary>
    /// Gets the election script: it manages election leadership acquisition, resignation, and reading operations.
    /// </summary>
    public static RespScript Election { get; } = Load("elections");

    /// <summary>
    /// Gets the claim script: it manages claim acquisition and status checking operations.
    /// </summary>
    public static RespScript Claim { get; } = Load("claims");

    /// <summary>
    /// Loads a script from the embedded resource <c>NCode.Coordination.Resp.Scripts.&lt;name&gt;.lua</c>.
    /// </summary>
    /// <param name="name">The name of the script, without its folder and extension.</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">The assembly has no such resource.</exception>
    public static RespScript Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var text = ReadResource(name);

        return new RespScript(name, text);
    }

    /// <summary>
    /// Loads a record script: the start that every record script shares, a line feed and the named script's text.
    /// </summary>
    /// <param name="name">The name of the script, without its folder and extension.</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">The assembly has no such resource.</exception>
    public static RespScript LoadRecord(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var prelude = ReadResource(RecordPrelude);
        var body = ReadResource(name);

        return new RespScript(name, prelude + "\n" + body);
    }

    /// <summary>
    /// Reads the text of an embedded script resource.
    /// </summary>
    /// <param name="name">The name of the script, without its folder and extension.</param>
    /// <returns>The text of the resource.</returns>
    /// <exception cref="InvalidOperationException">The assembly has no such resource.</exception>
    private static string ReadResource(string name)
    {
        var resourceName = ResourcePrefix + name + ResourceSuffix;
        using var stream = typeof(RespScript).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{resourceName}' does not exist."
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Checks that a constructor argument is neither null, empty nor white space.
    /// </summary>
    /// <param name="value">The argument.</param>
    /// <param name="paramName">The name of the argument.</param>
    /// <returns>The argument.</returns>
    /// <exception cref="ArgumentNullException">The argument is null.</exception>
    /// <exception cref="ArgumentException">The argument is empty or white space.</exception>
    private static string ValidateNotBlank(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        return value;
    }

    /// <summary>
    /// Gets the name of the script.
    /// </summary>
    public string Name { get; } = ValidateNotBlank(name, nameof(name));

    /// <summary>
    /// Gets the Lua source of the script.
    /// </summary>
    public string Text { get; } = ValidateNotBlank(text, nameof(text));
}
