using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime
{
    internal sealed partial class ProviderServer
    {

        internal static JsonObject TestEnvelope(
            ProviderFailureException failure, string operation = "resolve_and_act") =>
            TestFailureEnvelope(failure, operation);

        internal static JsonObject TestFailureEnvelope(
            Exception failure, string operation = "resolve_and_act") =>
            FailureEnvelope("test-1", operation, failure,
                operation is "resolve_and_act" or "window_action");
    }
}

// No FlaUI execution: only satisfies the linked attribution helper's type test.
namespace FlaUI.Core.Exceptions
{
    internal sealed class ElementNotEnabledException : Exception;
}
