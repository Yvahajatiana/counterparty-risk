namespace CounterpartyRisk.Domain.Contracts.Financing;

// Closed vocabularies of the financing model (#43, #45, ADR-002 §14). The aggregate itself
// (Facility, Drawing) belongs to the Portfolio module (#43).

/// <summary>Off-balance-sheet category of CRR3 Annex I. Stored, never the CCF itself.</summary>
public enum OffBalanceSheetCategory
{
    /// <summary>100 %: credit substitutes, acceptances, forward asset purchases.</summary>
    C1 = 1,

    /// <summary>50 %: transaction-related contingencies, NIF / RUF.</summary>
    C2 = 2,

    /// <summary>40 %: other commitments, including undrawn credit lines.</summary>
    C3 = 3,

    /// <summary>20 %: short-term self-liquidating trade finance.</summary>
    C4 = 4,

    /// <summary>10 %: unconditionally cancellable commitments (UCC).</summary>
    C5 = 5,
}

/// <summary>
/// Factor that prevents an unconditionally cancellable commitment from being classified in C5
/// (EBA/RTS/2025/06 on CRR art. 111(8), applied in anticipation, ADR-002 §14).
/// </summary>
public enum CancellationConstraint
{
    None = 1,

    RiskManagementDeficiency = 2,

    CommercialConsideration = 3,

    ReputationalRisk = 4,

    LitigationRisk = 5,
}

public enum FacilityStatus
{
    /// <summary>Contractual offer not yet accepted (CRR art. 111(4)): drawn 0, undrawn = offered amount.</summary>
    Offered = 1,

    Active = 2,

    /// <summary>Undrawn forced to 0.</summary>
    Frozen = 3,

    /// <summary>Undrawn forced to 0.</summary>
    Cancelled = 4,
}

public enum FacilityProductType
{
    TermLoan = 1,

    RevolvingCommitted = 2,

    RevocableLine = 3,

    FinancialGuarantee = 4,

    PerformanceGuarantee = 5,

    DocumentaryCredit = 6,
}
