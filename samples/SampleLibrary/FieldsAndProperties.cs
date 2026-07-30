namespace SampleLibrary;

public class FieldsAndProperties
{
    public static int StaticField;
    public int InstanceField;
    public readonly int ReadonlyField = 42;
    public const int ConstField = 100;

    private static int _staticPropBacking;
    public static int StaticProperty
    {
        get => _staticPropBacking;
        set => _staticPropBacking = value;
    }

    public int AutoProperty { get; set; }

    private int _propBacking;
    public int PropertyWithBody
    {
        get { return _propBacking; }
        set { _propBacking = value; }
    }

    public int ExpressionBodiedProperty => InstanceField;

    public int InitOnlyProperty { get; init; }

    public void ReadFields()
    {
        _ = StaticField;
        _ = InstanceField;
        _ = ReadonlyField;
        _ = ConstField;
    }

    public void WriteFields()
    {
        StaticField = 1;
        InstanceField = 2;
    }

    public void ReadWriteProperties()
    {
        AutoProperty = 10;
        _ = AutoProperty;
        PropertyWithBody = 20;
        _ = PropertyWithBody;
        _ = ExpressionBodiedProperty;
        _ = StaticProperty;
    }
}
