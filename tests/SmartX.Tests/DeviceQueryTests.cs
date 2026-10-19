using SmartX.Shared.Dto;
using SmartX.Shared.Enums;
using SmartX.Shared.Search;

namespace SmartX.Tests;

/// <summary>Search and filter rules shared by the API and the dashboard.</summary>
public class DeviceQueryTests
{
    private static readonly List<DeviceDto> Devices =
    [
        Make("Pump A", "24:6F:28:A1:00:01", DeviceKind.WaterPump, SensorCategory.Actuator, "Zone 1", DeviceStatus.Active),
        Make("Moisture Sensor B", "24:6F:28:B1:00:01", DeviceKind.MoistureSensor, SensorCategory.Environmental, "Zone 1", DeviceStatus.Fault, "MOISTURE_LOW"),
        Make("Smart Plug 1", "24:6F:28:D3:00:01", DeviceKind.SmartPlug, SensorCategory.PowerConsumption, "Zone 3", DeviceStatus.Disconnected, "DISCONNECTED")
    ];

    [Theory]
    [InlineData("pump", "Pump A")]
    [InlineData("SENSOR b", "Moisture Sensor B")]
    [InlineData("d3:00", "Smart Plug 1")]
    [InlineData("moisture_low", "Moisture Sensor B")]
    public void Text_search_matches_name_mac_or_error_code_case_insensitively(string text, string expected)
    {
        var query = new DeviceQuery { Search = text };

        Assert.Equal([expected], Devices.Where(query.Matches).Select(d => d.Name));
    }

    [Fact]
    public void Zone_text_and_filters_combine()
    {
        Assert.Equal(2, Devices.Count(new DeviceQuery { Search = "zone 1" }.Matches));
        Assert.Equal(["Pump A"], Devices.Where(new DeviceQuery { Search = "zone 1", Category = SensorCategory.Actuator }.Matches).Select(d => d.Name));
        Assert.Equal(["Smart Plug 1"], Devices.Where(new DeviceQuery { Status = DeviceStatus.Disconnected }.Matches).Select(d => d.Name));
        Assert.Equal(["Moisture Sensor B"], Devices.Where(new DeviceQuery { Kind = DeviceKind.MoistureSensor }.Matches).Select(d => d.Name));
    }

    [Fact]
    public void Alerts_only_keeps_devices_with_active_errors()
    {
        var query = new DeviceQuery { AlertsOnly = true };

        Assert.Equal(["Moisture Sensor B", "Smart Plug 1"], Devices.Where(query.Matches).Select(d => d.Name));
    }

    [Fact]
    public void Empty_query_matches_everything()
    {
        var query = new DeviceQuery();

        Assert.True(query.IsEmpty);
        Assert.Equal(3, Devices.Count(query.Matches));
        Assert.Equal("(all devices)", query.Describe());
    }

    private static DeviceDto Make(string name, string mac, DeviceKind kind, SensorCategory category, string zone, DeviceStatus status, params string[] errors) => new()
    {
        Id = Guid.NewGuid(),
        MacAddress = mac,
        Name = name,
        Kind = kind,
        Category = category,
        ZoneName = zone,
        IsSimulated = true,
        IsControllable = false,
        IsOn = false,
        Status = status,
        SensorType = "",
        Unit = "",
        ActiveErrorCodes = errors.ToList()
    };
}
