namespace Shark.DependencyInjection
{
    public interface INamedServiceFactory<TService>
        where TService : class
    {
        TService GetService(string name);
    }
}
