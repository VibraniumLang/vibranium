namespace Vibranium;

[global::System.AttributeUsage(global::System.AttributeTargets.Method | global::System.AttributeTargets.Property, Inherited = false)]
public sealed class MutAttribute : global::System.Attribute;

[global::System.AttributeUsage(global::System.AttributeTargets.ReturnValue | global::System.AttributeTargets.Parameter | global::System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class LifetimeOfAttribute(params string[] parameters) : global::System.Attribute
{
    public string[] Parameters { get; } = parameters;
}

/// <summary>Marks a trusted platform leaf of Vibranium.Std.Sys. The symbol must match the versioned hosted-ABI catalog.</summary>
[global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
public sealed class NativeAttribute(string symbol) : global::System.Attribute
{
    public string Symbol { get; } = symbol;
}
