using SmartX.Shared.Models;

namespace SmartX.Api.Services;

public interface ISensorRepository
{
    SensorRegistration Add(SensorRegistration sensor);
    IReadOnlyList<SensorRegistration> GetAll();
    SensorRegistration? GetById(Guid id);
    bool MacAddressExists(string macAddress);
    SensorAttachment? AddAttachment(Guid sensorId, SensorAttachment attachment);
}
