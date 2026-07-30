namespace SampleLibrary;

public class GenericClass<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _map = new();

    public void Add(TKey key, TValue value)
    {
        _map.Add(key, value);
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        return _map.TryGetValue(key, out value);
    }
}

public static class GenericMethods
{
    public static T? Default<T>() where T : struct
    {
        return default(T);
    }

    public static void Swap<T>(ref T a, ref T b)
    {
        (a, b) = (b, a);
    }
}
