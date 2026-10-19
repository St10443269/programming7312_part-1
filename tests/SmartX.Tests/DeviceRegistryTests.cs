using SmartX.Api.RealTime;
using SmartX.Shared.Enums;

namespace SmartX.Tests;

/// <summary>Dictionary&lt;MAC, device&gt; registration and lookup.</summary>
public class DeviceRegistryTests
{
    [Fact]
    public void Registered_device_is_found_by_mac_regardless_of_case_or_separator()
    {
        var net = new TestNetwork();
        net.AddDevice("24:6F:28:AB:CD:01", "Plug", DeviceKind.SmartPlug, "Zone 1");

        Assert.True(net.Registry.TryGet("24-6f-28-ab-cd-01", out var device));
        Assert.Equal("Plug", device.Name);
        Assert.Equal("24:6F:28:AB:CD:01", device.MacAddress);
    }

    [Fact]
    public void Same_mac_cannot_be_registered_twice()
    {
        var net = new TestNetwork();
        net.AddDevice("24:6F:28:AB:CD:01", "First", DeviceKind.SmartPlug, "Zone 1");

        var duplicate = new SimulatedDevice(Guid.NewGuid(), "24:6f:28:ab:cd:01", "Second", DeviceKind.Relay, "Zone 2");

        Assert.False(net.Registry.TryRegister(duplicate));
        Assert.Equal(1, net.Registry.Count);
        Assert.True(net.Registry.TryGet("24:6F:28:AB:CD:01", out var kept));
        Assert.Equal("First", kept.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("24:6F:28:00:00:99")]
    public void Unknown_or_blank_mac_returns_false_instead_of_throwing(string? mac)
    {
        var net = new TestNetwork().WithDemoDevices();

        Assert.False(net.Registry.TryGet(mac, out _));
    }

    [Fact]
    public void Demo_seeder_registers_simulated_devices_in_both_registries()
    {
        var registry = new DeviceRegistry(TimeProvider.System);
        var sensors = new SmartX.Api.Services.SensorRepository();

        DemoNetworkSeeder.Seed(registry, sensors);

        Assert.Equal(9, registry.Count);
        Assert.Equal(9, sensors.GetAll().Count);
        Assert.All(registry.GetAll(), d => Assert.True(d.IsSimulated));
        Assert.All(registry.GetAll(), d => Assert.NotNull(sensors.GetById(d.Id)));
    }
}
