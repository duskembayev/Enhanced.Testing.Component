namespace Enhanced.Testing.Component;

internal interface IConcurrentStartDependency : IComponentDependency
{
    bool StartConcurrently { get; }
}
