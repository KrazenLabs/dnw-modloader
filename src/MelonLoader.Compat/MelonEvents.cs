using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MelonLoader
{
    // Priority-ordered subscriber list
    public abstract class MelonEventBase<T> : IDisposable where T : Delegate
    {
        public class MelonEventSubscriber
        {
            public T del;
            public bool unsubscribeOnFirstInvocation;
            public int priority;
            public MelonAssembly melonAssembly;
        }

        private sealed class Subscriber
        {
            public T Action;
            public int Priority;
            public bool OneShot;
            public MelonAssembly Owner;
        }

        private readonly List<Subscriber> _subscribers = new List<Subscriber>();
        public readonly bool oneTimeUse;
        private bool _invoked;

        public MelonEventBase(bool oneTimeUse = false) { this.oneTimeUse = oneTimeUse; }

        public bool Disposed { get; private set; }

        public void Subscribe(T action, int priority = 0, bool unsubscribeOnFirstInvocation = false)
        {
            if (Disposed || action == null) return;
            var owner = MelonAssembly.GetMelonAssemblyOfMember(action.Method, action.Target);
            lock (_subscribers)
            {
                if (_subscribers.Any(s => s.Action == (Delegate)action)) return;
                int index = _subscribers.FindIndex(s => s.Priority > priority);
                _subscribers.Insert(index < 0 ? _subscribers.Count : index, new Subscriber { Action = action, Priority = priority, OneShot = unsubscribeOnFirstInvocation, Owner = owner });
            }

            if (owner != null) owner.OnUnregister.Subscribe(() => Unsubscribe(action), 0, true);

            if (oneTimeUse && _invoked) SafeInvoke(action);
        }

        public void Unsubscribe(T action)
        {
            if (action == null) return;
            lock (_subscribers) _subscribers.RemoveAll(s => s.Action == (Delegate)action);
        }

        public void Unsubscribe(MethodInfo method, object obj = null)
        {
            if (method == null) return;
            lock (_subscribers) _subscribers.RemoveAll(s => s.Action.Method == method && (obj == null || ReferenceEquals(s.Action.Target, obj)));
        }

        public void UnsubscribeAll()
        {
            lock (_subscribers) _subscribers.Clear();
        }

        public bool CheckIfSubscribed(MethodInfo method, object obj = null)
        {
            if (method == null) return false;
            lock (_subscribers) return _subscribers.Any(s => s.Action.Method == method && (obj == null || ReferenceEquals(s.Action.Target, obj)));
        }

        public MelonEventSubscriber[] GetSubscribers()
        {
            lock (_subscribers)
            {
                return _subscribers.Select(s => new MelonEventSubscriber
                {
                    del = s.Action,
                    unsubscribeOnFirstInvocation = s.OneShot,
                    priority = s.Priority,
                    melonAssembly = s.Owner,
                }).ToArray();
            }
        }

        protected void Invoke(Action<T> delegateInvoker)
        {
            if (Disposed) return;
            _invoked = true;

            Subscriber[] snapshot;
            lock (_subscribers) snapshot = _subscribers.ToArray();

            foreach (var subscriber in snapshot)
            {
                try { delegateInvoker(subscriber.Action); }
                catch (Exception e) { MelonLogger.Error("A subscriber of " + GetType().Name + " threw: " + e); }
                if (subscriber.OneShot) Unsubscribe(subscriber.Action);
            }
        }

        private void SafeInvoke(T action)
        {
            try { DynamicInvoke(action); }
            catch (Exception e) { MelonLogger.Error("A late subscriber of " + GetType().Name + " threw: " + e); }
        }

        protected virtual void DynamicInvoke(T action) { action.DynamicInvoke(); }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            UnsubscribeAll();
        }
    }

    public class MelonEvent : MelonEventBase<LemonAction>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke() { Invoke(a => a()); }
    }

    public class MelonEvent<T1> : MelonEventBase<LemonAction<T1>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1) { Invoke(a => a(arg1)); }
        protected override void DynamicInvoke(LemonAction<T1> action) { action(default(T1)); }
    }

    public class MelonEvent<T1, T2> : MelonEventBase<LemonAction<T1, T2>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2) { Invoke(a => a(arg1, arg2)); }
        protected override void DynamicInvoke(LemonAction<T1, T2> action) { action(default(T1), default(T2)); }
    }

    public class MelonEvent<T1, T2, T3> : MelonEventBase<LemonAction<T1, T2, T3>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2, T3 arg3) { Invoke(a => a(arg1, arg2, arg3)); }
        protected override void DynamicInvoke(LemonAction<T1, T2, T3> action) { action(default(T1), default(T2), default(T3)); }
    }

    public class MelonEvent<T1, T2, T3, T4> : MelonEventBase<LemonAction<T1, T2, T3, T4>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4) { Invoke(a => a(arg1, arg2, arg3, arg4)); }
        protected override void DynamicInvoke(LemonAction<T1, T2, T3, T4> action) { action(default(T1), default(T2), default(T3), default(T4)); }
    }

    public class MelonEvent<T1, T2, T3, T4, T5> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5) { Invoke(a => a(arg1, arg2, arg3, arg4, arg5)); }
        protected override void DynamicInvoke(LemonAction<T1, T2, T3, T4, T5> action) { action(default(T1), default(T2), default(T3), default(T4), default(T5)); }
    }

    public class MelonEvent<T1, T2, T3, T4, T5, T6> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5, T6>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6) { Invoke(a => a(arg1, arg2, arg3, arg4, arg5, arg6)); }
        protected override void DynamicInvoke(LemonAction<T1, T2, T3, T4, T5, T6> action) { action(default(T1), default(T2), default(T3), default(T4), default(T5), default(T6)); }
    }

    public class MelonEvent<T1, T2, T3, T4, T5, T6, T7> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5, T6, T7>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7) { Invoke(a => a(arg1, arg2, arg3, arg4, arg5, arg6, arg7)); }
        protected override void DynamicInvoke(LemonAction<T1, T2, T3, T4, T5, T6, T7> action) { action(default(T1), default(T2), default(T3), default(T4), default(T5), default(T6), default(T7)); }
    }

    public class MelonEvent<T1, T2, T3, T4, T5, T6, T7, T8> : MelonEventBase<LemonAction<T1, T2, T3, T4, T5, T6, T7, T8>>
    {
        public MelonEvent(bool oneTimeUse = false) : base(oneTimeUse) { }
        public void Invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8) { Invoke(a => a(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8)); }
        protected override void DynamicInvoke(LemonAction<T1, T2, T3, T4, T5, T6, T7, T8> action) { action(default(T1), default(T2), default(T3), default(T4), default(T5), default(T6), default(T7), default(T8)); }
    }

    public static class MelonEvents
    {
        public static readonly MelonEvent OnPreInitialization = new MelonEvent(true);
        public static readonly MelonEvent OnApplicationEarlyStart = new MelonEvent(true);
        public static readonly MelonEvent OnPreModsLoaded = new MelonEvent(true);
        public static readonly MelonEvent OnPreSupportModule = new MelonEvent(true);
        public static readonly MelonEvent OnApplicationStart = new MelonEvent(true);
        public static readonly MelonEvent OnApplicationLateStart = new MelonEvent(true);
        public static readonly MelonEvent OnApplicationQuit = new MelonEvent(true);
        public static readonly MelonEvent OnApplicationDefiniteQuit = new MelonEvent(true);
        public static readonly MelonEvent OnUpdate = new MelonEvent();
        public static readonly MelonEvent OnFixedUpdate = new MelonEvent();
        public static readonly MelonEvent OnLateUpdate = new MelonEvent();
        public static readonly MelonEvent OnGUI = new MelonEvent();
        public static readonly MelonEvent<int, string> OnSceneWasLoaded = new MelonEvent<int, string>();
        public static readonly MelonEvent<int, string> OnSceneWasInitialized = new MelonEvent<int, string>();
        public static readonly MelonEvent<int, string> OnSceneWasUnloaded = new MelonEvent<int, string>();
    }
}
