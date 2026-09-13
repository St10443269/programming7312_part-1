using SmartX.Shared.Enums;

namespace SmartX.Shared.Validation;

/// <summary>One problem found while recursively walking a deployment tree, tagged with the path to the offending node.</summary>
public sealed record ValidationIssue(string NodePath, ValidationSeverity Severity, string Message);
