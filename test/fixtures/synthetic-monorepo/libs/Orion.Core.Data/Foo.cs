namespace Orion.Core.Data;

/// <summary>Simple value type used by Parser and tests.</summary>
public record Foo(int Id, string Name)
{
    public bool IsValid => Id > 0 && !string.IsNullOrWhiteSpace(Name);
}
