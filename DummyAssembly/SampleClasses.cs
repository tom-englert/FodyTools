﻿using TomsToolbox.Essentials;


[assembly: PluginModule("1", "2", "3")]
[module: PluginModule("4", "5", "6")]

namespace FodyTools
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;

    using ReferencedAssembly;

    [TypeConverter(typeof(TomsToolbox.Essentials.Disposable))]
    [Sequence(1)]
    public class SimpleSampleClass
    {
        private static readonly int[] _staticArray = new[] {1, 2, 3, 4, 5};

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

        public void MethodWithExceptionHandler()
        {
            try
            {
                StaticClass.Method1();
            }
            catch (CustomException)
            {
                throw new CustomException("Test");
            }
            catch
            {
                // else do nothing
            }
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
                _current = new Structure { Value1 = "1" };
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

        public static SimpleGenericClass<T> FromSingleItemAndList<T, TItem>(T singleItem, IList<TItem> list)
            where TItem : T
        {
            return typeof(T) == typeof(TItem) ? new SimpleGenericClass<T>() : null;
        }
    }

    public class SimpleGenericClass<T> : IComparer<T>
    {
        public void Method(Func<T> argument)
        {
            SR.GuardNotNull(argument);
        }

        public void Method2<T1>(Func<T> argument, T1 arg1)
        {

        }

        int IComparer<T>.Compare(T x, T y)
        {
            return Comparer.Default.Compare(x, y);
        }
    }

    [SimpleAttribute]
    [SimpleAttribute(SimpleEnum.Value2)]
    public class ComplexSampleClass<T1, T2> : TomsToolbox.Essentials.WeakEventListener<T1, T2, EventArgs>
        where T1 : TomsToolbox.Essentials.DelegateComparer<T2>
        where T2 : class, TomsToolbox.Essentials.ITimeService
    {
        public ComplexSampleClass(T1 target, T2 source, Action<T1, object, EventArgs> onEventAction)
            : base(target, source, onEventAction, null, null)
        {
        }

        public ComplexSampleClass(T1 target, T2 source, Action<T1, object, EventArgs> onEventAction, Action<WeakEventListener<T1, T2, EventArgs>, T2> onAttachAction, Action<WeakEventListener<T1, T2, EventArgs>, T2> onDetachAction)
            : base(target, source, onEventAction, onAttachAction, onDetachAction)
        {
        }

        public T SomeMethod<T>(TomsToolbox.Essentials.AutoWeakIndexer<T1, T1> p1, Func<T2> p2, Func<T> p3)
            where T : TomsToolbox.Essentials.DelegateComparer<AutoWeakIndexer<int, string>>
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

        public T SomeMethod<T>(TomsToolbox.Essentials.WeakEventListener<T, object, EventArgs> p1)
            where T : TomsToolbox.Essentials.DelegateComparer<AutoWeakIndexer<int, string>>
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
            var y = default(TomsToolbox.Essentials.DelegateComparer<AutoWeakIndexer<int, string>>).SafeCast<WeakEventListener<DelegateComparer<AutoWeakIndexer<int, string>>, object, EventArgs>>();

            var x = SomeMethod(y);
        }
    }
}