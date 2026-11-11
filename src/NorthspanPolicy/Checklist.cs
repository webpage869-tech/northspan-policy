namespace NorthspanPolicy;

/// <summary>One requirement an AI acceptable-use policy is checked against.</summary>
public sealed record ChecklistItem(string Id, string Area, string Title, string Why, string Standard);

/// <summary>
/// The Northspan checklist, mapped to ISO/IEC 42001:2023 and NIST AI RMF 1.0.
/// Keep in step with Appendix C of the master policy template.
/// </summary>
public static class Checklist
{
    public static readonly IReadOnlyList<ChecklistItem> Items =
    [
        new("purpose", "Policy foundations", "Purpose and principles",
            "States why the policy exists and the principles staff must follow.",
            "ISO/IEC 42001 5.2, A.2.2 · NIST GOVERN 1"),
        new("scope", "Policy foundations", "Scope covers all staff, contractors and devices",
            "Without a clear scope, contractors and personal devices fall outside the rules.",
            "ISO/IEC 42001 4.3 · NIST GOVERN 1"),
        new("review", "Policy foundations", "Regular review cycle",
            "AI tools change quickly; the policy must set how often it is reviewed.",
            "ISO/IEC 42001 A.2.4 · NIST GOVERN 1"),
        new("tools", "Tools and data", "List of approved AI tools",
            "Staff need to know exactly which tools are allowed for work.",
            "ISO/IEC 42001 A.9.2, A.9.4 · NIST MAP 1"),
        new("datalevels", "Tools and data", "Data levels for each tool",
            "Sets what kind of information each tool may receive.",
            "ISO/IEC 42001 A.7 · NIST MAP 1"),
        new("personaldata", "Tools and data", "Rules on personal and confidential data",
            "The biggest real-world AI risk is staff pasting personal or confidential data into public tools.",
            "ISO/IEC 42001 A.7 · NIST GOVERN 1"),
        new("humanreview", "People and decisions", "Human review of AI output",
            "AI can be confidently wrong; a person must check output before it is used.",
            "ISO/IEC 42001 A.9.2 · NIST MAP 3"),
        new("decisions", "People and decisions", "No decisions about people by AI alone",
            "Decisions on credit, claims, hiring or discipline must be made by a person.",
            "ISO/IEC 42001 A.5 · NIST MANAGE 1"),
        new("roles", "People and decisions", "Named roles and responsibilities",
            "Someone must own the policy, approve tools and handle incidents.",
            "ISO/IEC 42001 5.3, A.3.2 · NIST GOVERN 2"),
        new("risk", "Risk and vendors", "Risk levels or impact assessment",
            "Higher-risk AI uses need stronger controls than everyday drafting.",
            "ISO/IEC 42001 6.1, 8.2, 8.4 · NIST MAP 1, MAP 5"),
        new("vendors", "Risk and vendors", "Vendor and third-party checks",
            "Many AI risks come from suppliers and the terms they set.",
            "ISO/IEC 42001 A.10.2, A.10.3 · NIST GOVERN 6"),
        new("training", "Oversight", "Staff training",
            "A policy staff have never been trained on is rarely followed.",
            "ISO/IEC 42001 7.2, 7.3 · NIST GOVERN 4"),
        new("incidents", "Oversight", "Incident reporting",
            "Staff need a clear way to report data entered into the wrong tool or harmful output.",
            "ISO/IEC 42001 A.3.3, A.8.4 · NIST MANAGE 4"),
        new("monitoring", "Oversight", "Monitoring and records",
            "Auditors and examiners will ask for evidence the policy is followed.",
            "ISO/IEC 42001 7.5, 9.1 · NIST MEASURE 3"),
        new("enforcement", "Oversight", "Consequences for breaking the policy",
            "Links the policy to the organization's disciplinary process.",
            "ISO/IEC 42001 A.2.3 · NIST GOVERN 1"),
    ];

    public static ChecklistItem? Find(string id) =>
        Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
}
