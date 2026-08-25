using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence;

/// <summary>
/// Development seed configuration, bound from the <c>Seed</c> section by the host.
/// </summary>
/// <param name="Enabled">When false, seeding is skipped entirely.</param>
/// <param name="ParentEmail">Email of the demo parent account.</param>
/// <param name="ParentPassword">Plaintext password, hashed via the delegate passed to the seeder.</param>
/// <param name="ParentDisplayName">Display name of the demo parent account.</param>
public sealed record SeedSettings(bool Enabled, string ParentEmail, string ParentPassword, string ParentDisplayName);

/// <summary>
/// Creates the demo parent account so a fresh development database is usable immediately.
/// Never creates devices or location data.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Inserts the configured parent if it is missing. Idempotent: an existing account with the same
    /// normalised email is left untouched (the password is never reset). Assumes the schema is already
    /// migrated — this method does not call <c>Migrate()</c>.
    /// </summary>
    /// <param name="db">Context to seed.</param>
    /// <param name="settings">Seed configuration.</param>
    /// <param name="hashPassword">
    /// Password hashing delegate, injected so this project does not depend on the API project.
    /// </param>
    /// <param name="logger">Logger for the outcome of the seed.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task SeedAsync(AppDbContext db, SeedSettings settings,
                                       Func<string, string> hashPassword, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hashPassword);
        ArgumentNullException.ThrowIfNull(logger);

        if (!settings.Enabled)
        {
            logger.LogDebug("Database seeding is disabled.");
            return;
        }

        var email = settings.ParentEmail?.Trim() ?? string.Empty;
        if (email.Length == 0 || string.IsNullOrEmpty(settings.ParentPassword))
        {
            logger.LogWarning("Database seeding is enabled but Seed:ParentEmail or Seed:ParentPassword is empty; skipping.");
            return;
        }

        var emailNormalized = email.ToLowerInvariant();

        if (await db.Parents.AnyAsync(p => p.EmailNormalized == emailNormalized, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Seed parent {Email} already exists; nothing to do.", emailNormalized);
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(settings.ParentDisplayName)
            ? email
            : settings.ParentDisplayName.Trim();

        var parent = new Parent
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailNormalized = emailNormalized,
            PasswordHash = hashPassword(settings.ParentPassword),
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Parents.Add(parent);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Seeded parent {Email} with id {ParentId}.", emailNormalized, parent.Id);
        }
        catch (DbUpdateException ex)
        {
            // Another instance seeded the same account between the check and the insert; the unique
            // index on email_normalized did its job, so drop our copy and carry on.
            db.Entry(parent).State = EntityState.Detached;
            logger.LogInformation(ex, "Seed parent {Email} was created concurrently; skipping.", emailNormalized);
        }
    }
}
