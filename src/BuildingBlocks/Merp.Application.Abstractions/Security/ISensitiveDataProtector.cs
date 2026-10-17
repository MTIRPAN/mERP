namespace Merp.Application.Abstractions.Security;

/// <summary>
/// Encrypts special category personal data (health, disability, criminal record…) before it is stored, field by
/// field (D13, D38). Keys belong to the current tenant; the domain only ever holds the encrypted text.
/// </summary>
public interface ISensitiveDataProtector
{
    /// <summary>The encrypted form of <paramref name="value"/>; null and blank stay null.</summary>
    string? Protect(string? value);

    /// <summary>The plain text of a value <see cref="Protect"/> returned; null stays null.</summary>
    string? Unprotect(string? protectedValue);
}
