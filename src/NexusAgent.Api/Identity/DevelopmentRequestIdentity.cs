namespace NexusAgent.Api.Identity;

public sealed class DevelopmentRequestIdentity
{
    public string RequesterId => "local-developer";
    public string TenantId => "local-development";
}