using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexusAgent.Application.Tools;

namespace NexusAgent.Application.Approvals;

public static class ApprovalActionFactory
{
    public static ApprovalAction Create<TInput>(
        ToolRequest<TInput> request,
        ToolCaller caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        if (string.IsNullOrWhiteSpace(caller.TenantId) ||
            string.IsNullOrWhiteSpace(caller.RequesterId) ||
            string.IsNullOrWhiteSpace(request.ToolName))
        {
            throw new ArgumentException(
                "Approval actions require tenant, requester, and tool.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            request.Input);

        var tenantBytes = Encoding.UTF8.GetBytes(caller.TenantId);
        var requesterBytes = Encoding.UTF8.GetBytes(caller.RequesterId);
        var toolBytes = Encoding.UTF8.GetBytes(request.ToolName);

        using var stream = new MemoryStream();

        WriteField(stream, tenantBytes);
        WriteField(stream, requesterBytes);
        WriteField(stream, toolBytes);
        WriteField(stream, payload);

        var hash = SHA256.HashData(stream.ToArray());

        return new ApprovalAction(
            caller.TenantId,
            caller.RequesterId,
            request.ToolName,
            Convert.ToHexString(hash));
    }

    private static void WriteField(
        Stream stream,
        byte[] value)
    {
        var length = BitConverter.GetBytes(value.Length);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(length);
        }

        stream.Write(length);
        stream.Write(value);
    }
}