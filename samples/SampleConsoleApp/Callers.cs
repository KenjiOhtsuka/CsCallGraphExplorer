using SampleLibrary;

namespace SampleConsoleApp;

public class Callers
{
    private readonly PublicMethods _methods = new();
    private readonly FieldsAndProperties _fields = new();
    private readonly Overloads _overloads = new();
    private readonly DerivedClass _derived = new();

    public void RunAll()
    {
        CallInstanceMethod();
        CallStaticMethod();
        CallOverloads();
        CallInheritance();
        CallFieldAccess();
        CallPropertyAccess();
        CallConstructors();
        CallDelegates();
        CallAsyncMethods();
        CallNestedTypes();
        CallStaticClass();
        CallGenerics();

        var internals = new Internals();
        internals.CallInternal();
    }

    private void CallInstanceMethod()
    {
        _methods.InstanceMethod("hello");
    }

    private static void CallStaticMethod()
    {
        PublicMethods.StaticMethod("world");
    }

    private void CallOverloads()
    {
        _ = _overloads.Compute();
        _ = _overloads.Compute(42);
        _ = _overloads.Compute(1, "two");
    }

    private void CallInheritance()
    {
        _ = _derived.Greet("user");
        _derived.CallBaseMethod();

        IProcessor processor = _derived;
        _ = processor.Process("data");
    }

    private void CallFieldAccess()
    {
        _fields.ReadFields();
        _fields.WriteFields();
    }

    private void CallPropertyAccess()
    {
        _fields.ReadWriteProperties();
    }

    private void CallConstructors()
    {
        var withDefault = new CtorsAndStatics();
        var withName = new CtorsAndStatics("test");
        _ = withDefault.GetName();
        CtorsAndStatics.ResetCounter();
    }

    private void CallDelegates()
    {
        var ld = new LambdasAndDelegates();
        _ = ld.UseDelegate(5);
        _ = ld.UseFunc(10);
        ld.LocalFunctionExample();
    }

    private async void CallAsyncMethods()
    {
        var asyncObj = new AsyncStuff();
        _ = await asyncObj.ComputeAsync(7);
        _ = await asyncObj.FetchAsync("example.com");
        asyncObj.FireAndForget();
        _ = await asyncObj.GetValueAsync();
    }

    private void CallNestedTypes()
    {
        var outer = new OuterClass(1);
        outer.OuterMethod();
        OuterClass.InnerStaticClass.DoSomething();
    }

    private void CallStaticClass()
    {
        StaticClass.Increment();
        _ = StaticClass.GetCount();
    }

    private void CallGenerics()
    {
        var map = new GenericClass<string, int>();
        map.Add("one", 1);
        _ = map.TryGet("one", out _);

        int a = 1, b = 2;
        GenericMethods.Swap(ref a, ref b);
    }
}
