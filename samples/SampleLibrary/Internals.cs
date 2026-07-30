using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SampleConsoleApp")]

namespace SampleLibrary;

public class Internals
{
    internal static string InternalStaticMethod()
    {
        return "internal";
    }

    private static string PrivateStaticMethod()
    {
        return "private";
    }

    public string CallInternal()
    {
        return InternalStaticMethod();
    }

    public string CallPrivate()
    {
        return PrivateStaticMethod();
    }
}
