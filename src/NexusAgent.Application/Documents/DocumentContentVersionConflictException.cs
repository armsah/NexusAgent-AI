namespace NexusAgent.Application.Documents;

public sealed class DocumentContentVersionConflictException
    : Exception
{
    public DocumentContentVersionConflictException()
        : base("Document content version conflict.")
    {
    }

    public DocumentContentVersionConflictException(
        Exception innerException)
        : base("Document content version conflict.", innerException)
    {
    }
}
