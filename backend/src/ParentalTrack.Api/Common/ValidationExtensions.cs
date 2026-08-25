namespace ParentalTrack.Api.Common;

/// <summary>
/// Input checks shared by the endpoints. Deliberately conservative: anything these reject is
/// rejected with a 400 before it reaches a service or the database.
/// </summary>
public static class ValidationExtensions
{
    private const int MaxEmailLength = 256;
    private const int MaxLocalPartLength = 64;
    private const int MaxDomainLength = 255;
    private const int MaxDomainLabelLength = 63;

    private const int MinPasswordLength = 10;
    private const int MaxPasswordLength = 128;

    /// <summary>Characters RFC 5322 allows in an unquoted local part, besides letters and digits.</summary>
    private const string LocalPartSymbols = "!#$%&'*+/=?^_`{|}~-";

    public static bool IsValidEmail(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var email = value.Trim();
        if (email.Length > MaxEmailLength)
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == email.Length - 1 || at != email.LastIndexOf('@'))
        {
            return false;
        }

        return IsValidLocalPart(email.AsSpan(0, at)) && IsValidDomain(email.AsSpan(at + 1));
    }

    /// <summary>Returns <c>(false, reason)</c> with a message safe to show the user.</summary>
    public static (bool Ok, string? Error) ValidatePassword(this string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return (false, "Password is required.");
        }

        if (value.Length < MinPasswordLength)
        {
            return (false, $"Password must be at least {MinPasswordLength} characters long.");
        }

        if (value.Length > MaxPasswordLength)
        {
            return (false, $"Password must be at most {MaxPasswordLength} characters long.");
        }

        var hasLetter = false;
        var hasDigit = false;
        foreach (var c in value)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }

            if (hasLetter && hasDigit)
            {
                return (true, null);
            }
        }

        return (false, "Password must contain at least one letter and at least one digit.");
    }

    private static bool IsValidLocalPart(ReadOnlySpan<char> local)
    {
        if (local.Length > MaxLocalPartLength || local[0] == '.' || local[^1] == '.')
        {
            return false;
        }

        for (var i = 0; i < local.Length; i++)
        {
            var c = local[i];
            if (c == '.')
            {
                if (local[i - 1] == '.')
                {
                    return false;
                }

                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c) && !LocalPartSymbols.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidDomain(ReadOnlySpan<char> domain)
    {
        if (domain.Length > MaxDomainLength || domain[^1] == '-' || domain[^1] == '.')
        {
            return false;
        }

        var labelLength = 0;
        var dots = 0;

        for (var i = 0; i < domain.Length; i++)
        {
            var c = domain[i];
            if (c == '.')
            {
                if (labelLength == 0 || domain[i - 1] == '-')
                {
                    return false;
                }

                dots++;
                labelLength = 0;
                continue;
            }

            if (labelLength == 0 && c == '-')
            {
                return false;
            }

            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }

            if (++labelLength > MaxDomainLabelLength)
            {
                return false;
            }
        }

        // At least one dot: "user@localhost" is not an address a parent can receive mail at.
        return dots >= 1 && labelLength > 0;
    }
}
