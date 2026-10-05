namespace Merp.Web.Errors;

/// <summary>Body of a 201 response: the id of the created record.</summary>
public sealed record CreatedResponse(Guid Id);
