namespace SmartX.Shared.Enums;

/// <summary>Tier of a node within a deployment tree, e.g. Facility A -&gt; Zone 1 -&gt; Sub-Zone B -&gt; Device.</summary>
public enum DeploymentNodeKind
{
    Facility,
    Zone,
    SubZone,
    Device
}
