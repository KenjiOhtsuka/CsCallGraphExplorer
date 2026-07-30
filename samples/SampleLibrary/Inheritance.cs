namespace SampleLibrary;

public interface IProcessor
{
    string Process(string input);
}

public class BaseClass
{
    public virtual string Greet(string name)
    {
        return $"Hello, {name}";
    }

    public void BaseOnlyMethod()
    {
    }
}

public class DerivedClass : BaseClass, IProcessor
{
    public override string Greet(string name)
    {
        var baseResult = base.Greet(name);
        return $"{baseResult}!";
    }

    public string Process(string input)
    {
        return input.ToUpper();
    }

    public void CallBaseMethod()
    {
        BaseOnlyMethod();
    }
}

public struct Point : IProcessor
{
    public int X { get; set; }
    public int Y { get; set; }

    public string Process(string input)
    {
        return $"{input} ({X},{Y})";
    }
}
