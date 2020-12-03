namespace Shark.Options
{
    public class GenericOptions<TService>
        where TService : class
    {
        public string Name { get; set; }
    }
}
