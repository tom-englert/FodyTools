﻿#pragma warning disable CS8603 // Possible null reference return.

using TomsToolbox.Essentials;

[assembly: PluginModule("1", "2", "3")]
[module: PluginModule("4", "5", "6")]

namespace DummyNullableAssembly
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;

    using ReferencedAssembly;

    using TomsToolbox.Essentials;

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

        public void MethodWithExceptionHandler()
        {
            try
            {

            }
            catch (CustomException)
            {
                throw;
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

        }

        public void Method2<T1>(Func<T> argument, T1 arg1)
        {

        }

        int IComparer<T>.Compare(T x, T y)
        {
            return Comparer.Default.Compare(x, y);
        }
    }
}