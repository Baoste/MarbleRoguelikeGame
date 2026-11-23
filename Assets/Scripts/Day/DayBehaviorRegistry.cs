using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MarblesECS
{
    internal delegate Task DayAction(DayActionContext context, CancellationToken cancellation);

    /// <summary>Registers explicitly named operations and validates their parameters at content load.</summary>
    public sealed class DayBehaviorRegistry
    {
        private readonly Dictionary<string, Func<string, DayAction>> actions = new Dictionary<string, Func<string, DayAction>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<string, Func<DayLifecycleNotice, bool>>> conditions =
            new Dictionary<string, Func<string, Func<DayLifecycleNotice, bool>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<string, Func<IDayFeature>>> features =
            new Dictionary<string, Func<string, Func<IDayFeature>>>(StringComparer.Ordinal);

        public void RegisterAction<T>(string type, Func<string, T> readParameters,
            Func<DayActionContext, T, CancellationToken, Task> execute)
        {
            if (readParameters == null) throw new ArgumentNullException(nameof(readParameters));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            actions.Add(type, raw =>
            {
                T parameters = readParameters(raw);
                return (context, token) => execute(context, parameters, token);
            });
        }

        public void RegisterCondition<T>(string type, Func<string, T> readParameters, Func<DayLifecycleNotice, T, bool> evaluate)
        {
            if (readParameters == null) throw new ArgumentNullException(nameof(readParameters));
            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
            conditions.Add(type, raw =>
            {
                T parameters = readParameters(raw);
                return notice => evaluate(notice, parameters);
            });
        }

        public void RegisterFeature<T>(string type, Func<string, T> readParameters, Func<T, IDayFeature> create)
        {
            if (type == "Attribute") throw new ArgumentException("Attribute is a built-in rule type.", nameof(type));
            if (readParameters == null) throw new ArgumentNullException(nameof(readParameters));
            if (create == null) throw new ArgumentNullException(nameof(create));
            features.Add(type, raw =>
            {
                T parameters = readParameters(raw);
                return () => create(parameters);
            });
        }

        internal DayAction ReadAction(string type, string raw, string path) => Bind(actions, type, raw, path);
        internal Func<DayLifecycleNotice, bool> ReadCondition(string type, string raw, string path) => Bind(conditions, type, raw, path);
        internal Func<IDayFeature> ReadFeature(string type, string raw, string path) => Bind(features, type, raw, path);

        private static T Bind<T>(Dictionary<string, Func<string, T>> registrations, string type, string raw, string path)
        {
            if (string.IsNullOrWhiteSpace(type) || !registrations.TryGetValue(type, out var bind))
                throw new ArgumentException($"{path}.type: unknown type '{type}'.");
            try { return bind(raw ?? string.Empty); }
            catch (Exception error) { throw new ArgumentException($"{path}.parameters: {error.Message}", error); }
        }
    }

    /// <summary>One scoped activation. The factory creates a new instance for every activation.</summary>
    public interface IDayFeature : IDisposable
    {
        void Activate(DayProgressSnapshot day);
        void Deactivate();
    }
}
