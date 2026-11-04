using Microsoft.Extensions.Compliance.Classification;

namespace CounterpartyRisk.SharedKernel.Compliance;

/// <summary>
/// Data classification taxonomy. Classified values are redacted by the logging pipeline
/// (configured in ServiceDefaults) so that they never reach logs or traces in clear text.
/// </summary>
public static class CounterpartyRiskTaxonomy
{
    public static string TaxonomyName { get; } = typeof(CounterpartyRiskTaxonomy).FullName!;

    /// <summary>Personal data (names, e-mails, user identifiers).</summary>
    public static DataClassification PersonalData { get; } = new(TaxonomyName, nameof(PersonalData));

    /// <summary>Confidential business data (portfolio amounts, counterparty identifiers, tokens).</summary>
    public static DataClassification SensitiveData { get; } = new(TaxonomyName, nameof(SensitiveData));
}

/// <summary>Marks a parameter or property as personal data.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class PersonalDataAttribute : DataClassificationAttribute
{
    public PersonalDataAttribute()
        : base(CounterpartyRiskTaxonomy.PersonalData)
    {
    }
}

/// <summary>Marks a parameter or property as sensitive business data.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class SensitiveDataAttribute : DataClassificationAttribute
{
    public SensitiveDataAttribute()
        : base(CounterpartyRiskTaxonomy.SensitiveData)
    {
    }
}
