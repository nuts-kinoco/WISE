using System;

namespace WISE.Domain.Interfaces;

public interface IJobSerializer
{
    string Serialize<T>(T jobConfiguration);
    T? Deserialize<T>(string serializedConfiguration);
    object? Deserialize(string serializedConfiguration, Type type);
}
