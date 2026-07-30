namespace SampleLibrary;

public static class StaticClass
{
    public static int GlobalCounter;

    public static void Increment()
    {
        GlobalCounter++;
    }

    public static int GetCount()
    {
        return GlobalCounter;
    }
}
