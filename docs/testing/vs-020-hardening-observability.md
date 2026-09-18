# VS-020 Hardening And Observability Tests

## Required Gates

- full solution restore and build;
- existing unit and integration smoke suites;
- VS-019 E2E load bot flow;
- VS-020 100 bot acceptance profile;
- VS-020 hardening profile;
- release soak profile for 7200 seconds;
- Unity Windows QA profiling run at 1920x1080 targeting 60 FPS.

## Local Commands

```bash
dotnet restore Divinity.sln
dotnet build Divinity.sln --configuration Release --no-restore
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release --no-build -- --profile acceptance
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release --no-build -- --profile hardening --soak-duration-seconds 1
```

The full soak gate uses:

```bash
dotnet run --project tools/load-bots/Divinity.LoadBots.csproj --configuration Release --no-build -- --profile soak
```

## Coverage

The hardening profile validates:

- category rate limits;
- payload larger than 64 KiB;
- truncated Protobuf;
- anonymous handshake spam;
- move through wall;
- repeated movement sequence;
- target outside map;
- cast without MP;
- client cooldown zero;
- attack out of range;
- equip missing item;
- repeated reward grant;
- two connections for one character;
- reconnect during monster death without duplicate reward;
- client clock change against ticket TTL;
- structured log minimum fields and secret scan;
- memory and GC smoke checks.

## Unity QA Profiling

Run on a Windows QA machine with the project opened by Unity:

```bash
Unity.exe -batchmode -quit -projectPath apps/game-client-unity -executeMethod Divinity.Editor.DivinityQaProfileSmokeTest.Run
```

This bootstrap verifies that the QA target is configured for 1920x1080 and 60 FPS and records memory and GC timing. A human QA pass must still observe the rendered client scene during the release gate.
