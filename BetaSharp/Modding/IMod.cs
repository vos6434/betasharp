namespace BetaSharp.Modding;

public interface IMod
{
    public string Name { get; }
    public string Description { get; }
    public string Author { get; }
    public Side Side { get; }
    public void Initialize();
    public void PostInitialize();
    public void Unload();
}
