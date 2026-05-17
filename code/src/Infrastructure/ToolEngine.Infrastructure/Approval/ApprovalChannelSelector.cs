namespace ToolEngine.Infrastructure.Approval;

using Microsoft.Extensions.Options;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Selects the correct IApprovalChannel for a given tenant + risk combination.
///
/// Risk override: Critical always forces EmailOtp regardless of tenant preference,
/// because OTP provides explicit identity confirmation for irreversible actions.
///
/// Fallback: if the preferred channel has no registered implementation,
/// DashboardChannel is used as the safe silent fallback.
/// </summary>
public sealed class ApprovalChannelSelector
{
    private readonly IReadOnlyDictionary<ApprovalChannelType, IApprovalChannel> _channels;
    private readonly ApprovalOptions _options;

    public ApprovalChannelSelector(
        IEnumerable<IApprovalChannel> channels,
        IOptions<ApprovalOptions>     options)
    {
        _channels = channels.ToDictionary(c => c.ChannelType);
        _options  = options.Value;
    }

    public IApprovalChannel Select(string tenantId, ApprovalRisk risk)
    {
        // Critical risk always requires OTP — forced regardless of tenant config.
        if (risk == ApprovalRisk.Critical)
            return Get(ApprovalChannelType.EmailOtp);

        // Tenant override takes precedence over global default.
        if (_options.TenantChannelOverrides.TryGetValue(tenantId, out var tenantChannel))
            return Get(tenantChannel);

        return Get(_options.DefaultChannel);
    }

    private IApprovalChannel Get(ApprovalChannelType type) =>
        _channels.TryGetValue(type, out var ch) ? ch
            : _channels.GetValueOrDefault(ApprovalChannelType.Dashboard)
              ?? throw new InvalidOperationException("DashboardChannel is not registered.");
}
