namespace SampleLibrary;

public class CtorsAndStatics
{
    public static int StaticCounter;

    static CtorsAndStatics()
    {
        StaticCounter = 0;
    }

    public CtorsAndStatics() : this("default")
    {
    }

    public CtorsAndStatics(string name)
    {
        _name = name;
        StaticCounter++;
    }

    private readonly string _name;

    public string GetName() => _name;

    public static void ResetCounter()
    {
        StaticCounter = 0;
    }
}
