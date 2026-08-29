using SonyBraviaControl.Models;

namespace SonyBraviaControl.Services;

public interface ISettingsStore
{
    BraviaSettings Load();
    void Save(BraviaSettings settings);
}
