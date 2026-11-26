// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace NCode.Coordination.Resp.Server;

/// <summary>
/// A Lua script that the provider sends to the server, loaded from the assembly's embedded resources.
/// </summary>
internal sealed class RespScript
{
    private const string ResourcePrefix = "NCode.Coordination.Resp.Scripts.";
    private const string ResourceSuffix = ".lua";

    /// <summary>
    /// Initializes a new instance of the <see cref="RespScript"/> class.
    /// </summary>
    /// <param name="name">The name of the script.</param>
    /// <param name="text">The Lua source of the script.</param>
    /// <exception cref="ArgumentNullException">The name or the text is null.</exception>
    /// <exception cref="ArgumentException">The name or the text is empty or white space.</exception>
    public RespScript(string name, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Name = name;
        Text = text;
    }

    /// <summary>
    /// Gets the startup probe: it proves the server runs Lua and lets the user call TIME.
    /// </summary>
    public static RespScript Probe { get; } = Load("probe");

    /// <summary>
    /// Gets the name of the script.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the Lua source of the script.
    /// </summary>
    public string Text { get; }

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

        return new RespScript(name, reader.ReadToEnd());
    }
}
