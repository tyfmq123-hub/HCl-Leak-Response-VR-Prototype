using System;
using UnityEngine;

namespace HClTraining.App
{
    /// <summary>
    /// Temporarily limits asynchronous asset integration to the Low (2 ms) player budget.
    /// Unity ignores this budget in the Editor. Awake/Start and scene activation can still hitch.
    /// </summary>
    public sealed class SceneLoadingBudget : IDisposable
    {
        readonly ThreadPriority previousPriority;
        bool disposed;

        public SceneLoadingBudget()
        {
            previousPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Do not overwrite a newer setting owned by another system.
            if (Application.backgroundLoadingPriority == ThreadPriority.Low)
                Application.backgroundLoadingPriority = previousPriority;
        }
    }
}
