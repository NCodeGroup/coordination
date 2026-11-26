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
    /// Gets the startup probe: it proves the server runs Lua and lets the user run scripts,
    /// and returns the server section of INFO.
    /// </summary>
    public static RespScript Probe { get; } = Load("probe");

    /// <summary>
    /// Gets the counter script: it applies the increments of a batch in order, and stops after the first
    /// whose count exceeds its limit.
    /// </summary>
    public static RespScript CounterIncrement { get; } = Load("counter-increment");

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

        var resourceName = ResourcePrefix + name + ResourceSuffix;
        using var stream = typeof(RespScript).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{resourceName}' does not exist."
            );
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var text = reader.ReadToEnd();
        return new RespScript(name, text);
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
