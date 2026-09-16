# VS-009 Map Import Test Notes

Run the content and map gates from the repository root:

```bash
dotnet run --project tools/content-builder/tests/Divinity.ContentBuilder.Tests.csproj --configuration Release
dotnet run --project tools/content-builder/Divinity.ContentBuilder.csproj --configuration Release -- build --content-root content --output-root tools/content-builder/artifacts
dotnet run --project packages/test-fixtures/map-tests/Divinity.MapTests.csproj --configuration Release
```

The VS-009 map tests cover:

- valid Training Field map validation;
- spawn outside bounds rejection;
- missing safe spawn rejection;
- inconsistent collision wall blocked-cell rejection;
- matching Unity/server content hash;
- incompatible `JoinWorld` content hash rejection in `world-runtime`;
- safe spawn inside the safe region and outside blocked cells;
- Unity visual artifact JSON load from `Assets/StreamingAssets`.

Optional Unity batch smoke, when Unity is available:

```bash
/Applications/Unity/Hub/Editor/2021.3.26f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath apps/game-client-unity -executeMethod Divinity.Editor.DivinityMapVisualSmokeTest.Run -logFile -
```
