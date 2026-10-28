using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    /// <summary>
    /// External event handler that runs a delegate on the Revit API thread and keeps its result.
    /// The completion signal is reset in <see cref="Prepare"/> (before the event is raised),
    /// so a fast Execute cannot be missed by <see cref="WaitForCompletion"/>.
    /// </summary>
    public class DelegateEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly string _name;
        private Func<UIApplication, object> _work;

        public object Result { get; private set; }
        public Exception Error { get; private set; }

        public DelegateEventHandler(string name)
        {
            _name = name;
        }

        public void Prepare(Func<UIApplication, object> work)
        {
            _work = work;
            Result = null;
            Error = null;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                Result = _work(app);
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public string GetName()
        {
            return _name;
        }
    }
}
