using System;
using UnityEditor;
using UnityEngine;

namespace Divinity.Editor
{
    public static class DivinityQaProfileSmokeTest
    {
        public static void Run()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.SetResolution(1920, 1080, fullscreen: false);

            var frameBudgetMs = 1000d / 60d;
            var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
            var started = DateTime.UtcNow;
            _ = GC.GetTotalMemory(forceFullCollection: false);
            var gcSampleMs = (DateTime.UtcNow - started).TotalMilliseconds;
            var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);

            if (frameBudgetMs > 16.7d)
            {
                throw new InvalidOperationException("QA frame budget exceeds the 60 FPS target.");
            }

            if (gcSampleMs > 4d)
            {
                throw new InvalidOperationException($"GC sample exceeded the VS-020 spike budget: {gcSampleMs:F2}ms.");
            }

            if (memoryAfter - memoryBefore > 32L * 1024L * 1024L)
            {
                throw new InvalidOperationException("Managed memory grew beyond the VS-020 smoke budget.");
            }

            Debug.Log($"VS-020 QA profiling smoke passed. target=1920x1080 fps=60 frameBudgetMs={frameBudgetMs:F2} memoryDeltaBytes={memoryAfter - memoryBefore} gcSampleMs={gcSampleMs:F2}");
            EditorApplication.Exit(0);
        }
    }
}
