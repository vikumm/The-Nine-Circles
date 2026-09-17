using System;
using Divinity.GameRules.Movement;
using UnityEditor;
using UnityEngine;

namespace Divinity.Editor
{
    public static class DivinityPredictionVisualSmokeTest
    {
        public static void Run()
        {
            var predictor = new ClientMovementPredictor(ClientPredictionSettings.Default);
            predictor.Reset(8, 8, CardinalDirection.South);

            var previousRenderX = predictor.State.RenderX;
            for (var frame = 1UL; frame <= 60UL; frame++)
            {
                predictor.ApplyLocalInput(PredictedMoveIntent.Direction(frame, frame, 1, 0));
                if (predictor.State.RenderX <= previousRenderX)
                {
                    throw new InvalidOperationException("Prediction did not move the visual position immediately.");
                }

                previousRenderX = predictor.State.RenderX;
            }

            if (Math.Abs(predictor.State.RenderY - 8d) > MovementMath.Epsilon)
            {
                throw new InvalidOperationException("Horizontal prediction drifted on the Y axis.");
            }

            Debug.Log("VS-011 prediction visual smoke passed.");
            EditorApplication.Exit(0);
        }
    }
}
