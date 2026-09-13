using System.Text.RegularExpressions;
using SmartX.Shared.Enums;
using SmartX.Shared.Models;

namespace SmartX.Shared.Validation;

/// <summary>
/// Recursively validates a nested device deployment tree (Sub-Zone -&gt; Zone
/// -&gt; Facility style configuration profiles). A flat loop cannot walk a
/// tree of unknown, uneven depth - recursion is the natural fit: each call
/// validates one node's own rules, then recurses into its children,
/// threading a shared "seen MAC addresses" set down so duplicates are
/// caught anywhere in the whole tree, not just among siblings.
/// </summary>
public static class DeploymentTreeValidator
{
    private static readonly Regex MacAddressPattern =
        new(@"^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$", RegexOptions.Compiled);

    /// <summary>Maximum sane nesting depth before we assume the config is malformed (e.g. a cyclical import).</summary>
    private const int MaxDepth = 8;

    public static IReadOnlyList<ValidationIssue> Validate(DeploymentNode root)
    {
        var issues = new List<ValidationIssue>();
        var seenMacAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ValidateNode(root, path: root.Name, depth: 0, seenMacAddresses, issues);

        return issues;
    }

    /// <summary>
    /// Validates a single node's own rules, then recurses into every child
    /// with an incremented depth and the same shared issue/mac-address
    /// accumulators. This is the recursive step: the base case is a
    /// <see cref="DeploymentNodeKind.Device"/> leaf node, which has no
    /// children to descend into.
    /// </summary>
    private static void ValidateNode(
        DeploymentNode node,
        string path,
        int depth,
        HashSet<string> seenMacAddresses,
        List<ValidationIssue> issues)
    {
        if (depth > MaxDepth)
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Critical,
                $"Nesting exceeds the maximum supported depth of {MaxDepth} - possible malformed or cyclical config."));
            return; // stop recursing down a runaway branch
        }

        if (string.IsNullOrWhiteSpace(node.Name))
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Critical, "Node name cannot be blank."));
        }

        ValidateKindPlacement(node, path, depth, issues);

        if (node.Kind == DeploymentNodeKind.Device)
        {
            ValidateDeviceLeaf(node, path, seenMacAddresses, issues);
            return; // base case - devices are leaves, nothing further to recurse into
        }

        if (node.Children.Count == 0)
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Warning,
                $"'{node.Name}' has no child zones or devices configured."));
        }

        foreach (var child in node.Children)
        {
            ValidateNode(child, $"{path} -> {child.Name}", depth + 1, seenMacAddresses, issues);
        }
    }

    private static void ValidateKindPlacement(DeploymentNode node, string path, int depth, List<ValidationIssue> issues)
    {
        // Facility -> Zone -> Sub-Zone -> Device is the only safe ordering the
        // gateway understands; a Zone nested directly under another Zone (or
        // any other skip/inversion) means the config was hand-edited badly.
        var expectedKindAtDepth = depth switch
        {
            0 => DeploymentNodeKind.Facility,
            1 => DeploymentNodeKind.Zone,
            _ => (DeploymentNodeKind?)null // Sub-Zone/Device can repeat or terminate below this depth
        };

        if (expectedKindAtDepth is { } expected && node.Kind != expected)
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Critical,
                $"Expected a {expected} node at depth {depth} but found {node.Kind}."));
        }
    }

    private static void ValidateDeviceLeaf(
        DeploymentNode node,
        string path,
        HashSet<string> seenMacAddresses,
        List<ValidationIssue> issues)
    {
        if (node.Children.Count > 0)
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Warning,
                "Device nodes should not have children; nested entries under it will be ignored."));
        }

        if (string.IsNullOrWhiteSpace(node.MacAddress))
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Critical, "Device is missing a MAC address."));
            return;
        }

        if (!MacAddressPattern.IsMatch(node.MacAddress))
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Critical,
                $"'{node.MacAddress}' is not a valid MAC address (expected AA:BB:CC:DD:EE:FF)."));
            return;
        }

        if (!seenMacAddresses.Add(node.MacAddress))
        {
            issues.Add(new ValidationIssue(path, ValidationSeverity.Critical,
                $"Duplicate MAC address '{node.MacAddress}' already used elsewhere in this deployment tree."));
        }
    }
}
