namespace SampleService.Services;

public class PeopleCache
{
    public IReadOnlyList<string> Names { get; private set; } = [];

    public void Load(IEnumerable<string> names) => Names = names.ToArray();
}
