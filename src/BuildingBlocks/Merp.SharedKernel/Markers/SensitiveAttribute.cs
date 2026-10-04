namespace Merp.SharedKernel.Markers;

/// <summary>
/// Sensitive personal data (KVKK). Without the related permission the value is masked in DTOs, and reads are
/// written to the audit log (D13).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class SensitiveAttribute : Attribute;
