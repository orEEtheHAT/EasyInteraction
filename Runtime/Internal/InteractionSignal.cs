using System;
using UnityEngine;

namespace EasyInteraction
{
    // Copy-on-subscribe keeps delivery allocation-free and stable during reentrant subscription changes.
    internal sealed class InteractionSignal<T>
    {
        private Action<T>[] listeners = Array.Empty<Action<T>>();
        public void Add(Action<T> callback)
        {
            if (callback == null)
                return;
            var next = new Action<T>[listeners.Length + 1];
            Array.Copy(listeners, next, listeners.Length);
            next[next.Length - 1] = callback;
            listeners = next;
        }

        public void Remove(Action<T> callback)
        {
            int index = Array.LastIndexOf(listeners, callback);
            if (index < 0)
                return;
            var next = new Action<T>[listeners.Length - 1];
            Array.Copy(listeners, 0, next, 0, index);
            Array.Copy(listeners, index + 1, next, index, listeners.Length - index - 1);
            listeners = next;
        }

        public void Send(T value, UnityEngine.Object context)
        {
            var snapshot = listeners;
            for (int i = 0; i < snapshot.Length; i++)
                try
                {
                    snapshot[i](value);
                }
                catch (Exception error)
                {
                    Debug.LogException(error, context);
                }
        }
    }
}
