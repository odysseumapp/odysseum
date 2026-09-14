namespace Odysseum.Server.API.Enums;

/// <summary>Serialized as camelCase strings ("draft", "revised", "done") on the wire and in project.json.</summary>
public enum DocumentStatus
{
    Draft,
    Revised,
    Done,
}
