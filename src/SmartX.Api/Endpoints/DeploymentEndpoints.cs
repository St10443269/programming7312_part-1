using SmartX.Shared.Enums;
using SmartX.Shared.Models;
using SmartX.Shared.Validation;

namespace SmartX.Api.Endpoints;

public static class DeploymentEndpoints
{
    public static RouteGroupBuilder MapDeploymentEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/validate", (DeploymentNode tree) =>
        {
            var issues = DeploymentTreeValidator.Validate(tree);
            var isValid = issues.All(i => i.Severity != ValidationSeverity.Critical);
            return Results.Ok(new { isValid, issues });
        });

        group.MapGet("/sample", () => Results.Ok(BuildSampleTree()));

        return group;
    }

    /// <summary>A ready-made Facility A -> Zone 1 -> Sub-Zone B tree the dashboard can load to demo the recursive validator.</summary>
    private static DeploymentNode BuildSampleTree() => new()
    {
        Name = "Facility A",
        Kind = DeploymentNodeKind.Facility,
        Children =
        [
            new DeploymentNode
            {
                Name = "Zone 1",
                Kind = DeploymentNodeKind.Zone,
                Children =
                [
                    new DeploymentNode
                    {
                        Name = "Sub-Zone B",
                        Kind = DeploymentNodeKind.SubZone,
                        Children =
                        [
                            new DeploymentNode { Name = "Hydroponic Node 1", Kind = DeploymentNodeKind.Device, MacAddress = "24:6F:28:AA:11:01" },
                            new DeploymentNode { Name = "Hydroponic Node 2", Kind = DeploymentNodeKind.Device, MacAddress = "24:6F:28:AA:11:02" }
                        ]
                    }
                ]
            },
            new DeploymentNode
            {
                Name = "Zone 2",
                Kind = DeploymentNodeKind.Zone,
                Children =
                [
                    new DeploymentNode { Name = "Meter Node 1", Kind = DeploymentNodeKind.Device, MacAddress = "24:6F:28:AA:22:01" }
                ]
            }
        ]
    };
}
