namespace CounterpartyRisk.Domain.Contracts.Identifiers;

// Opaque technical identifiers (#5 security checklist): GUID-based, never sequential, so an id
// exposed by the API cannot be guessed or enumerated. Each id is its own type, so a TradeId can
// never be passed where a NettingSetId is expected.

public readonly record struct CounterpartyId(Guid Value)
{
    public static CounterpartyId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

/// <summary>Group of counterparties sharing a GLEIF ultimate parent.</summary>
public readonly record struct CounterpartyGroupId(Guid Value)
{
    public static CounterpartyGroupId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

/// <summary>Legal entity of the (fictitious) reporting bank: the ABAC scope dimension (#9).</summary>
public readonly record struct LegalEntityId(Guid Value)
{
    public static LegalEntityId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct BusinessLineId(Guid Value)
{
    public static BusinessLineId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct DeskId(Guid Value)
{
    public static DeskId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct NettingSetId(Guid Value)
{
    public static NettingSetId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

/// <summary>Credit Support Annex (collateral agreement) attached to a netting set.</summary>
public readonly record struct CsaId(Guid Value)
{
    public static CsaId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct TradeId(Guid Value)
{
    public static TradeId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct CollateralId(Guid Value)
{
    public static CollateralId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

/// <summary>Financing facility (loan, credit line, guarantee), modelled by #43.</summary>
public readonly record struct FacilityId(Guid Value)
{
    public static FacilityId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct LimitId(Guid Value)
{
    public static LimitId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
