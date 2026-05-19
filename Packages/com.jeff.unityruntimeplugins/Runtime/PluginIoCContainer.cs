using System;
using System.Collections.Concurrent;

namespace UnityRuntimePlugins
{
    public interface IPluginIoCContainer
    {
        void Register<T>(string serviceName, T serviceInstance);
        void Unregister(string serviceName);
        T Resolve<T>(string serviceName);
        bool Contains(string serviceName);
    }

    /// <summary>
    /// A lightweight, thread-safe Inversion of Control (IoC) container used to register,
    /// manage, and resolve services or active plugin modules dynamically.
    /// </summary>
    public class PluginIoCContainer : IPluginIoCContainer
    {
        private static PluginIoCContainer _instance;
        public static PluginIoCContainer Instance => _instance ??= new PluginIoCContainer();

        private readonly ConcurrentDictionary<string, object> _services = new ConcurrentDictionary<string, object>();

        public void Register<T>(string serviceName, T serviceInstance)
        {
            if (string.IsNullOrEmpty(serviceName)) return;
            _services[serviceName] = serviceInstance;
        }

        public void Unregister(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return;
            _services.TryRemove(serviceName, out _);
        }

        public T Resolve<T>(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return default;
            if (_services.TryGetValue(serviceName, out var serviceInstance))
            {
                return (T)serviceInstance;
            }
            return default;
        }

        public bool Contains(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return false;
            return _services.ContainsKey(serviceName);
        }
    }
}
