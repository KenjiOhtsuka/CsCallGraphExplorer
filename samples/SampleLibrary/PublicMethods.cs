namespace SampleLibrary;

public class PublicMethods
{
    public void InstanceMethod(string input)
    {
        StaticMethod(input);
        PrivateMethod();
    }

    public static void StaticMethod(string input)
    {
        _ = input.Length;
    }

    public int MethodWithRefOut(ref int x, out int y)
    {
        y = x * 2;
        InternalMethod();
        return y;
    }

    internal void InternalMethod()
    {
        ProtectedMethod();
    }

    protected void ProtectedMethod()
    {
    }

    private void PrivateMethod()
    {
    }
}
