using SmartX.Shared.Enums;

namespace SmartX.Shared.Models;

/// <summary>
/// One node in a multi-tier deployment/configuration tree, e.g.
/// <c>Facility A -&gt; Zone 1 -&gt; Sub-Zone B -&gt; Device</c>. Nodes nest
/// arbitrarily deep, which is what makes recursive (rather than
/// flat/iterative) validation the natural fit in
/// <see cref="Validation.DeploymentTreeValidator"/>.
/// </summary>
public sealed class DeploymentNode
{
    public required string Name { get; init; }
    public required DeploymentNodeKind Kind { get; init; }

    /// <summary>Only populated for <see cref="DeploymentNodeKind.Device"/> leaf nodes.</summary>
    public string? MacAddress { get; init; }

    public List<DeploymentNode> Children { get; init; } = [];
}
