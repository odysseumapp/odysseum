namespace Odysseum.Server.Repositories;

public interface ITemplateRepository
{
    string Root { get; }
    void EnsureDefault();
    IReadOnlyList<ProjectTemplate> List();
    ProjectTemplate Get(string name);
    ProjectTemplate Save(ProjectTemplate template);
    void Delete(string name);
}
