// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NCode.Coordination.PostgreSql.Schema;

/// <summary>
/// A validated name of the PostgreSQL schema that holds the provider's tables, with its quoted form and its hash.
/// </summary>
internal sealed partial class PgSchemaName
{
    /// <summary>
    /// The longest schema name, which is PostgreSQL's limit on an identifier.
    /// </summary>
    private const int MaxLength = 63;

    /// <summary>
    /// The placeholder that a template holds where the schema's quoted name goes.
    /// </summary>
    private const string Placeholder = "{schema}";

    /// <summary>
    /// Matches a name: lowercase letters, digits and underscores, not starting with a digit.
    /// </summary>
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// Gets the default schema, named <c>coordination</c>.
    /// </summary>
    public static PgSchemaName Default { get; } = Create("coordination");

    /// <summary>
    /// Creates a schema name after validating it.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The schema name.</returns>
    /// <exception cref="ArgumentException">The name is not 1 to 63 lowercase letters, digits and underscores that
    /// start with a letter or an underscore.</exception>
    public static PgSchemaName Create(string name)
    {
        if (name is null || name.Length > MaxLength || !NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a schema name the provider takes: use 1 to 63 lowercase letters, digits and underscores, starting with a letter or an underscore.",
                nameof(name)
            );
        }

        return new PgSchemaName(name);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PgSchemaName"/> class from a validated name.
    /// </summary>
    /// <param name="name">The validated name.</param>
    private PgSchemaName(string name)
    {
        Span<byte> nameBytes = stackalloc byte[MaxLength];
        var written = Encoding.UTF8.GetBytes(name, nameBytes);
        var encoded = nameBytes[..written];

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(encoded, digest);
        var prefix = digest[..4];

        Name = name;
        Quoted = "\"" + name + "\"";
        Hash = Convert.ToHexStringLower(prefix);
    }

    /// <summary>
    /// Gets the schema's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the schema's name in double quotes, as SQL text takes it.
    /// </summary>
    public string Quoted { get; }

    /// <summary>
    /// Gets the first 8 lowercase hex digits of the SHA-256 of the name's UTF-8 bytes.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// Replaces every <c>{schema}</c> in a SQL template with the schema's quoted name.
    /// </summary>
    /// <param name="template">The SQL template.</param>
    /// <returns>The SQL text.</returns>
    /// <exception cref="ArgumentNullException">The template is null.</exception>
    public string Apply(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return template.Replace(Placeholder, Quoted, StringComparison.Ordinal);
    }
}
