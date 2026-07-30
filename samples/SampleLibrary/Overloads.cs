namespace SampleLibrary;

public class Overloads
{
    public int Compute()
    {
        return 0;
    }

    public int Compute(int x)
    {
        return x;
    }

    public int Compute(int x, string y)
    {
        return x + y.Length;
    }

    public int Compute(string a, string b)
    {
        return a.Length + b.Length;
    }

    public int Compute(ref int x)
    {
        return x++;
    }
}
