namespace SampleLibrary;

public delegate int Transformer(int x);

public class LambdasAndDelegates
{
    public int UseDelegate(int value)
    {
        Transformer square = x => x * x;
        return CallDelegate(square, value);
    }

    private int CallDelegate(Transformer t, int x)
    {
        return t(x);
    }

    public int UseFunc(int value)
    {
        Func<int, int> addOne = x => x + 1;
        return addOne(value);
    }

    public void LocalFunctionExample()
    {
        int Multiply(int a, int b) => a * b;

        var result = Multiply(3, 4);
        Console.WriteLine(result);
    }

    public int UseAnonymousMethod(int value)
    {
        Transformer triple = delegate (int x) { return x * 3; };
        return triple(value);
    }
}
