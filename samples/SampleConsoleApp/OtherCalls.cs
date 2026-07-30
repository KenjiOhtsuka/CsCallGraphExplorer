using SampleLibrary;

namespace SampleConsoleApp;

public class OtherCalls
{
    public void Execute()
    {
        // Also calls InstanceMethod — creates a second caller branch
        var methods = new PublicMethods();
        methods.InstanceMethod("from OtherCalls");

        // Also creates CtorsAndStatics — second caller branch
        var obj = new CtorsAndStatics("other");
        _ = obj.GetName();
    }

    public void ExtraCaller()
    {
        var methods = new PublicMethods();
        methods.InstanceMethod("from ExtraCaller");
        PublicMethods.StaticMethod("extra");
    }
}
