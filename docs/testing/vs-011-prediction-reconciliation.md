# VS-011 Prediction and Reconciliation Test Notes

Run the VS-011 prediction gate from the repository root:

```bash
dotnet restore packages/test-fixtures/prediction-tests/Divinity.PredictionTests.csproj
dotnet build packages/test-fixtures/prediction-tests/Divinity.PredictionTests.csproj --configuration Release --no-restore
dotnet run --project packages/test-fixtures/prediction-tests/Divinity.PredictionTests.csproj --configuration Release --no-build
```

The VS-011 prediction tests cover:

- reapplying pending inputs after an authoritative snapshot;
- discarding intents acknowledged by `ServerEnvelope.ack_sequence`;
- immediate local prediction for click-to-move targets;
- smooth correction for small visual error;
- snap correction for large visual error;
- server `Correction` overriding local prediction;
- diagonal tie preserving the last valid orientation;
- stale snapshot rejection;
- 60-frame visual responsiveness within the 60 FPS frame budget.

Optional Unity batch smoke, when Unity is available:

```bash
/Applications/Unity/Hub/Editor/2021.3.26f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath apps/game-client-unity -executeMethod Divinity.Editor.DivinityPredictionVisualSmokeTest.Run -logFile -
```

Unity consumes `packages/game-rules/Movement` as the local `com.divinity.movement-rules` package. The package contains visual prediction helpers only; server snapshots and corrections remain authoritative.
