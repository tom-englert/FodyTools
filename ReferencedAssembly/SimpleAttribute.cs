namespace ReferencedAssembly
{
    using System;

    public enum SimpleEnum
    {
        Value1,
        Value2,
        Value3
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
    public class SimpleAttribute : Attribute
    {
        public SimpleEnum SomeValue { get; }

        public SimpleAttribute()
        {
        }

        public SimpleAttribute(SimpleEnum someValue)
        {
            SomeValue = someValue;
        }
    }
}
