namespace UnityRuntimePlugins
{
    /// <summary>
    /// Defines a generic contract for components or runtime modules capable of handling dynamic function invocations.
    /// This decouples callers from implementation details such as WASM or direct C# invocations.
    /// </summary>
    public interface ICallablePlugin
    {
        void InvokeFunction(string functionName, params object[] args);
    }
}
