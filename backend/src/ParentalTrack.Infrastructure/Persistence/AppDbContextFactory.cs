using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParentalTrack.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct an <see cref="AppDbContext"/> without starting the API host, e.g.
/// <c>dotnet ef migrations add Init -p src/ParentalTrack.Infrastructure -s src/ParentalTrack.Api</c>
/// run from <c>backend/</c>.
/// </summary>
/// <remarks>
/// The connection string is resolved from, in order: the <c>ParentalTrack_ConnectionString</c>
/// environment variable, <c>appsettings.Development.json</c> then <c>appsettings.json</c> of the API
/// project (located by walking up from the current directory), and finally the local default.
/// The files are parsed directly rather than through a configuration builder so this project needs
/// no configuration packages at design time.
/// </remarks>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>Environment variable that overrides every other connection string source.</summary>
    public const string ConnectionStringEnvironmentVariable = "ParentalTrack_ConnectionString";

    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=parentaltrack;Username=parentaltrack;Password=parentaltrack";

    private const string ApiProjectDirectoryName = "ParentalTrack.Api";
    private const string SourceDirectoryName = "src";

    // Highest precedence last-wins order matches how the host layers its configuration files.
    private static readonly string[] SettingsFileNames = ["appsettings.Development.json", "appsettings.json"];

    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ResolveConnectionString())
            .Options;

        return new AppDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var apiDirectory = FindApiProjectDirectory(Directory.GetCurrentDirectory());
        if (apiDirectory is not null)
        {
            foreach (var fileName in SettingsFileNames)
            {
                var fromFile = ReadPostgresConnectionString(Path.Combine(apiDirectory, fileName));
                if (!string.IsNullOrWhiteSpace(fromFile))
                {
                    return fromFile;
                }
            }
        }

        return DefaultConnectionString;
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for the API project, so the command
    /// works from <c>backend/</c>, from either project directory, or from the repository root.
    /// </summary>
    private static string? FindApiProjectDirectory(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, ApiProjectDirectoryName, StringComparison.Ordinal))
            {
                return directory.FullName;
            }

            var sibling = Path.Combine(directory.FullName, ApiProjectDirectoryName);
            if (Directory.Exists(sibling))
            {
                return sibling;
            }

            var underSource = Path.Combine(directory.FullName, SourceDirectoryName, ApiProjectDirectoryName);
            if (Directory.Exists(underSource))
            {
                return underSource;
            }
        }

        return null;
    }

    private static string? ReadPostgresConnectionString(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                && connectionStrings.ValueKind is JsonValueKind.Object
                && connectionStrings.TryGetProperty("Postgres", out var postgres)
                && postgres.ValueKind is JsonValueKind.String)
            {
                return postgres.GetString();
            }

            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Design-time only: report and fall through to the next source rather than blocking migrations.
            Console.Error.WriteLine($"AppDbContextFactory: could not read '{path}': {ex.Message}");
            return null;
        }
    }
}
