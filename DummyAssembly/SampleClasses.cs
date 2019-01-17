using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;
using TomsToolbox.Core;

[assembly: PluginModule("1", "2", "3")]
[module: PluginModule("4", "5", "6")]

namespace FodyTools
{
    using ReferencedAssembly;
    using System;
    using System.ComponentModel;

    [Sequence(1)]
    public class SimpleSampleClass
    {
        [Sequence(2)]
        private readonly WeakEventSource<CancelEventArgs> _eventSource = new WeakEventSource<CancelEventArgs>();

        private readonly RealTimeService _realTimeService = new RealTimeService();

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

        IEnumerable<Structure> GetItems()
        {
            yield return new Structure { Value1 = "V1" };
            yield return new Structure { Value2 = "V2" };
        }

        class GetItemsImpl : IEnumerable<Structure>, IEnumerator<Structure>
        {
            private Structure _current;

            IEnumerator<Structure> IEnumerable<Structure>.GetEnumerator()
            {
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this;
            }

            bool IEnumerator.MoveNext()
            {
                _current = new Structure {Value1 = "1"};
                return true;
            }

            void IEnumerator.Reset()
            {
                _current = default(Structure);
            }

            Structure IEnumerator<Structure>.Current => _current;

            object IEnumerator.Current => _current;

            public void Dispose()
            {
            }
        }

        object GetItem()
        {
            return new Structure { Value2 = "V2" };
        }
    }

    public class SimpleGenericClass<T>
    {
        public void Method(Func<T> argument)
        {

        }

        public void Method2<T1>(Func<T> argument, T1 arg1)
        {

        }
    }

    public class ComplexSampleClass<T1, T2> : TomsToolbox.Core.WeakEventListener<T1, T2, EventArgs>
        where T1 : TomsToolbox.Core.DelegateComparer<T2>
        where T2 : class, TomsToolbox.Core.ITimeService
    {


        public ComplexSampleClass(T1 target, T2 source, Action<T1, object, EventArgs> onEventAction)
            : base(target, source, onEventAction, null, null)
        {
        }

        public ComplexSampleClass(T1 target, T2 source, Action<T1, object, EventArgs> onEventAction, Action<WeakEventListener<T1, T2, EventArgs>, T2> onAttachAction, Action<WeakEventListener<T1, T2, EventArgs>, T2> onDetachAction)
            : base(target, source, onEventAction, onAttachAction, onDetachAction)
        {
        }

        public ComplexSampleClass(T1 target, TomsToolbox.Core.WeakReference<T2> source, Action<T1, object, EventArgs> onEventAction, Action<WeakEventListener<T1, T2, EventArgs>, T2> onAttachAction, Action<WeakEventListener<T1, T2, EventArgs>, T2> onDetachAction)
            : base(target, source, onEventAction, onAttachAction, onDetachAction)
        {
        }

        public T SomeMethod<T>(TomsToolbox.Core.TryCastWorker<T1> p1, Func<T2> p2, Func<T> p3)
            where T : TomsToolbox.Core.DelegateComparer<AutoWeakIndexer<int, string>>
        {
            var x = new AutoWeakIndexer<int, string>(i => i.ToString());

            var comparer = x.Comparer;
            var keys = x.Keys;

            if (comparer != null && keys.IsReadOnly)
            {
                throw new Exception("never happens");
            }

            return default(T);
        }

        public T SomeMethod<T>(TomsToolbox.Core.TryCastWorker<T> p1)
            where T : TomsToolbox.Core.DelegateComparer<AutoWeakIndexer<int, string>>
        {
            var x = new AutoWeakIndexer<int, string>(i => i.ToString());

            var comparer = x.Comparer;
            var keys = x.Keys;

            if (comparer != null && keys.IsReadOnly)
            {
                throw new Exception("never happens");
            }

            return default(T);
        }

        public void AnotherMethod()
        {
            var y = default(TomsToolbox.Core.DelegateComparer<AutoWeakIndexer<int, string>>).TryCast();

            var x = SomeMethod(y);
        }
    }

}