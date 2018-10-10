using TomsToolbox.Core;

[assembly: PluginModule("1", "2", "3")]
[module: PluginModule("4", "5", "6")]

namespace DummyAssembly
{
    using System;
    using System.ComponentModel;

    [Sequence(1)]
    public class Class1
    {
        [Sequence(2)]
        private readonly WeakEventSource<CancelEventArgs> _eventSource = new WeakEventSource<CancelEventArgs>();

        private RealTimeService _realTimeService = new RealTimeService();

        [Sequence(3)]
        private int _field;

        [Sequence(4)]
        public void Method(int param)
        {
            var uri = AssemblyExtensions.GeneratePackUri(GetType().Assembly);

            _field = param;
            param += 1;
            if (param == _field)
            {
                _field -= 1;
            }

            switch (param)
            {
                    case 1:
                        break;

                    case 2:
                        param = param - 1;
                        break;

                    default:
                        _field = param - 2;
                        break;
            }
        }

        [Sequence(5)]
        public event EventHandler<CancelEventArgs> SomeWeakEvent
        {
            add => _eventSource.Subscribe(value);
            remove => _eventSource.Unsubscribe(value);
        }
    }
}