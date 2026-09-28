using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ScreenPortal.Editor
{
    /// <summary>Comando "test-editmode": roda os testes EditMode (domínio + composição) pelo Test Runner da Unity.</summary>
    internal static class TestRunnerCommand
    {
        [InitializeOnLoadMethod]
        private static void RegisterCommand() => EditorCommands.RegisterAsync("test-editmode", Run);

        private static void Run(Action<string> done)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(done, api));
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "ScreenPortal.Tests.EditMode", "ScreenPortal.Tests.Composition" },
            }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly Action<string> _done;
            private readonly TestRunnerApi _api;
            private readonly List<string> _failures = new List<string>();

            public Callbacks(Action<string> done, TestRunnerApi api)
            {
                _done = done;
                _api = api;
            }

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    _failures.Add(result.Test.FullName + ": " + result.Message);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _api.UnregisterCallbacks(this);
                string summary = $"{result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped";
                if (_failures.Count > 0) summary += " | " + string.Join(" | ", _failures);
                _done(summary);
            }
        }
    }
}
