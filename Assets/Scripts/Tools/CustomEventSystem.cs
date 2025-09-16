using System;
using System.Collections.Generic;


public class CustomEventSystem
{
    private static readonly Dictionary<Type, Delegate> _topics = new();

    // Subscribe
    public static void Subscribe<T>(Action<T> listener)
    {
        var t = typeof(T);
        if (_topics.TryGetValue(t, out var del))
            _topics[t] = (Action<T>)del + listener;
        else
            _topics[t] = listener;
    }

    // Unsubscribe
    public static void Unsubscribe<T>(Action<T> listener)
    {
        var t = typeof(T);
        if (_topics.TryGetValue(t, out var del))
        {
            var current = (Action<T>)del - listener;
            if (current == null) _topics.Remove(t);
            else _topics[t] = current;
        }
    }

    // Publish/raise an event
    public static void Publish<T>(T payload)
    {
        if (_topics.TryGetValue(typeof(T), out var del))
            ((Action<T>)del)?.Invoke(payload);
    }

    // Optional: clear a topic (useful between scenes/tests)
    public static void Clear<T>() => _topics.Remove(typeof(T));

    // Optional: clear everything
    public static void ClearAll() => _topics.Clear();
}
