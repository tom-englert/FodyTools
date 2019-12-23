namespace ReferencedAssembly
{
    using System;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
    public class SimpleAttribute : Attribute
    {
    }
}
