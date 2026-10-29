namespace Merp.Modules.Platform.Contracts.Notifications;

/// <summary>
/// A file another module attaches to the e-mail of a notification, e.g. the calendar invitation of an interview (R5,
/// D52). Kept small: it travels in the outbox and is stored with the queued e-mail.
/// </summary>
/// <param name="FileName">Name shown to the recipient, e.g. <c>invite.ics</c>.</param>
/// <param name="ContentType">Media type; <c>text/calendar; method=REQUEST</c> makes an invitation.</param>
/// <param name="Content">The bytes.</param>
public sealed record EmailFile(string FileName, string ContentType, byte[] Content);
