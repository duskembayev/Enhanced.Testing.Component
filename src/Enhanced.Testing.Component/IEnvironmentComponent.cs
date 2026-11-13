namespace Enhanced.Testing.Component;

internal interface IEnvironmentComponent : IComponent
{
    Task WaitForStartAsync();
}
