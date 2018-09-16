namespace DummyAssembly
{
    public class Class1
    {
        private int _field;

        public void Method(int param)
        {
            _field = param;
            param += 1;
            if (param == _field)
            {
                _field -= 1;
            }
        }
    }
}