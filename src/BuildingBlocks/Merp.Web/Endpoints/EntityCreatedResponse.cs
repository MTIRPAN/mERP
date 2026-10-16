namespace Merp.Web.Endpoints;

/// <summary>Id of a created record; its URL is in the Location header. Shared by the modules' create endpoints.</summary>
public sealed record EntityCreatedResponse(Guid Id);
