namespace Merp.SharedKernel.Markers;

/// <summary>Field level changes of this entity are written to the module's <c>audit_entries</c> table (D13).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AuditedAttribute : Attribute;
