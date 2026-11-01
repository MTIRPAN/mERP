namespace Merp.Modules.HR.Application;

/// <summary>AI features of the HR module; tenants enable them one by one (D24).</summary>
public static class HrAiFeatures
{
    /// <summary>A leave request form filled from what the employee wrote (docs/domain/yapay-zeka.md §3.11, D64).</summary>
    public const string LeaveRequestDraft = "hr.leave_request_draft";
}
