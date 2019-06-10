namespace FodyTools.Tests.UUTs
{
    using System.Diagnostics;

    public class SimpleTestClass
    {
        public void SimpleMethod(int i)
        {
            var m = 0;
            
            for (var k = 0; k < i; k++)
            {
                m += k + i;
            }

            Trace.WriteLine(m);
        }
    }
}