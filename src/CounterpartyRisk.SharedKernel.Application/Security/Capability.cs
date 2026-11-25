namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Function-level permission of the authorization matrix (threat model section 7.1).
/// A capability is granted through realm roles only (see <see cref="Policies"/>); the data scope
/// (legal entities, business lines) applies on top of every capability.
/// </summary>
public enum Capability
{
    ReadExposure = 1,
    ReadLimits = 2,
    ReadCva = 3,
    ReadRiskOversight = 4,
    ReadCapital = 5,
    ReadMarketAndBenchmark = 6,
    Export = 7,
    ManageLimitThresholds = 8,
    ManageLimits = 9,
    ApproveLimitChange = 10,
    ReadAudit = 11,
    ManageMethodVersions = 12,
    ApproveMethodVersion = 13,
    ManageJobRuns = 14,
    ManageHistoryCorrections = 15,
    ApproveHistoryCorrection = 16,
    ReadDataQualityDetail = 17,
}
