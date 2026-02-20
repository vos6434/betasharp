namespace BetaSharp.Modding;

public class ModSideAttribute : Attribute
{
    public Side Side { get; }

    public ModSideAttribute(Side side)
    {
        Side = side;
    }
}
