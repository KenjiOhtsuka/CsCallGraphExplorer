namespace SampleLibrary;

public record BaseRecord(string Name);

public record DerivedRecord(string Value) : BaseRecord(Value + "!");
