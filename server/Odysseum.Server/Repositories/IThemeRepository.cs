namespace Odysseum.Server.Repositories;

public interface IThemeRepository
{
    IReadOnlyList<Theme> List();
    Theme Get(string name);
    Theme Save(string name, Dictionary<string, string>? colors);
    void Delete(string name);
}
