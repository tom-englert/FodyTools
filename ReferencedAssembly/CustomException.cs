namespace ReferencedAssembly
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    [Simple(SimpleEnum.Value2)]
    public class CustomException : Exception
    {
        public CustomException()
        {
        }

        public CustomException(string message) : base(message)
        {
        }

        public CustomException(string message, Exception inner) : base(message, inner)
        {
        }

        protected CustomException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}