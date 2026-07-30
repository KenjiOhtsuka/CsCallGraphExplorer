namespace SampleLibrary;

public class OuterClass
{
    private int _outerValue;

    public OuterClass(int value)
    {
        _outerValue = value;
    }

    public void OuterMethod()
    {
        var inner = new InnerClass();
        inner.InnerMethod();
    }

    public class InnerClass
    {
        public void InnerMethod()
        {
            StaticHelper();
        }

        private static void StaticHelper()
        {
        }
    }

    public static class InnerStaticClass
    {
        public static void DoSomething()
        {
        }
    }
}
