using System;
using System.Collections.Generic;

namespace KungFuVania.Core
{
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> handlers = new();

        public static void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            handlers[type] = handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!handlers.TryGetValue(type, out var existing)) return;

            var combined = Delegate.Remove(existing, handler);
            if (combined == null)
                handlers.Remove(type);
            else
                handlers[type] = combined;
        }

        public static void Publish<T>(T eventData)
        {
            if (handlers.TryGetValue(typeof(T), out var existing))
                ((Action<T>)existing).Invoke(eventData);
        }
    }
}
