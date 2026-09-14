namespace Odysseum.Server.Settings;

public interface ISettingsProvider
{
    IServerSettings GetSettings(bool copy = false);
    void SaveSettings(IServerSettings settings);
    void SaveSettings();
    void DebugSettingsToLog();
}
