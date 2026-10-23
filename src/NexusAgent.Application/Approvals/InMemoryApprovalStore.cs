using System.Collections.Concurrent;

namespace NexusAgent.Application.Approvals;

public sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly ConcurrentDictionary<Guid, ApprovalRequest> _requests = new();
    private readonly object _sync = new();

    public Task<ApprovalRequest?> GetAsync(
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _requests.TryGetValue(approvalId, out var request);
            return Task.FromResult(request);
        }
    }

    public Task CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ApprovalId == Guid.Empty ||
            request.Status != ApprovalStatus.Pending ||
            request.Risk < ActionRisk.High ||
            request.CreatedAt >= request.ExpiresAt ||
            string.IsNullOrWhiteSpace(request.Action.TenantId) ||
            string.IsNullOrWhiteSpace(request.Action.RequesterId) ||
            string.IsNullOrWhiteSpace(request.Action.ToolName) ||
            string.IsNullOrWhiteSpace(request.Action.PayloadDigest))
        {
            throw new ArgumentException("Invalid approval request.");
        }

        lock (_sync)
        {
            if (!_requests.TryAdd(request.ApprovalId, request))
            {
                throw new InvalidOperationException(
                    "Duplicate approval identifier.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> DecideAsync(
        Guid approvalId,
        string approverId,
        bool approved,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_requests.TryGetValue(approvalId, out var request) ||
                request.Status != ApprovalStatus.Pending ||
                string.IsNullOrWhiteSpace(approverId) ||
                string.Equals(
                    request.Action.RequesterId,
                    approverId,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            if (now >= request.ExpiresAt)
            {
                _requests[approvalId] = request with
                {
                    Status = ApprovalStatus.Expired
                };

                return Task.FromResult(false);
            }

            _requests[approvalId] = request with
            {
                Status = approved
                    ? ApprovalStatus.Approved
                    : ApprovalStatus.Denied,
                ApproverId = approverId
            };

            return Task.FromResult(true);
        }
    }

    public Task<bool> ConsumeAsync(
        Guid approvalId,
        ApprovalAction expectedAction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedAction);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_requests.TryGetValue(approvalId, out var request) ||
                request.Status != ApprovalStatus.Approved)
            {
                return Task.FromResult(false);
            }

            if (now >= request.ExpiresAt)
            {
                _requests[approvalId] = request with
                {
                    Status = ApprovalStatus.Expired
                };

                return Task.FromResult(false);
            }

            if (request.Action != expectedAction)
            {
                return Task.FromResult(false);
            }

            _requests[approvalId] = request with
            {
                Status = ApprovalStatus.Consumed
            };

            return Task.FromResult(true);
        }
    }
}