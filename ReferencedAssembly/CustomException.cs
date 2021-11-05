namespace ReferencedAssembly
{
    using System;
    using System.Collections;
    using System.Runtime.Serialization;

    [Serializable]
    [Simple(SimpleEnum.Value2)]
    public class CustomException : Exception, IEnumerable
    {
        private static readonly int[] _staticArray = new[] { 5, 4, 3, 2, 1 };

        public CustomException()
        {
            SR.AnotherGuard();
            StaticClass.Method2();
        }

        public CustomException(string message) : base(message)
        {
            SR.GuardNotNull(message);
        }

        public CustomException(string message, Exception inner) : base(message, inner)
        {
        }

        protected CustomException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }

        public static void SomeMethod()
        {
            Console.WriteLine("SomeMethod");
        }

        static CustomException()
        {
            Console.WriteLine(".cctor");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return default;
        }
    }
}