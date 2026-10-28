using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ModelEditing
{
    /// <summary>
    /// Base for commands whose whole body runs on the Revit API thread as a delegate.
    /// </summary>
    public abstract class DelegateCommandBase : ExternalEventCommandBase
    {
        // Must stay below the MCP server's 2-minute request timeout.
        protected const int DefaultTimeoutMilliseconds = 110000;

        private readonly object _executionLock = new object();

        protected DelegateCommandBase(UIApplication uiApp, string handlerName)
            : base(new DelegateEventHandler(handlerName), uiApp)
        {
        }

        protected object RunInRevit(Func<Document, object> work, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            lock (_executionLock)
            {
                var handler = (DelegateEventHandler)Handler;
                handler.Prepare(app =>
                {
                    var doc = app.ActiveUIDocument?.Document
                              ?? throw new InvalidOperationException("No active Revit document.");
                    return work(doc);
                });

                if (!RaiseAndWaitForCompletion(timeoutMilliseconds))
                {
                    throw new TimeoutException(
                        $"{CommandName} timed out. Revit may be busy, showing a dialog, or in an edit mode (e.g. Edit Group), where external events do not run.");
                }

                if (handler.Error != null)
                {
                    throw new Exception($"{CommandName} failed: {handler.Error.Message}", handler.Error);
                }

                return handler.Result;
            }
        }
    }
}
