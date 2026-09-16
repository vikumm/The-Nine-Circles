# VS-010 Authoritative Movement Test Notes

Run the VS-010 movement gate from the repository root:

```bash
dotnet restore packages/test-fixtures/movement-tests/Divinity.MovementTests.csproj
dotnet build packages/test-fixtures/movement-tests/Divinity.MovementTests.csproj --configuration Release --no-restore
dotnet run --project packages/test-fixtures/movement-tests/Divinity.MovementTests.csproj --configuration Release --no-build
```

The VS-010 movement tests cover:

- diagonal direction normalization;
- 4.5 units/s max displacement per 20 Hz movement tick;
- click-to-move acceptance for a navigable target;
- wall-crossing rejection;
- click target outside map rejection;
- click target on blocked cell rejection;
- repeated sequence rejection;
- dead and stunned movement rejection;
- 10 Hz authoritative snapshot cadence;
- periodic, disconnect and graceful-shutdown checkpoints;
- Gateway `MoveIntent` rate limit of 20 messages per second;
- authenticated Gateway routing from `MoveIntent` to authoritative `WorldSnapshot`.

Unity remains a bootstrap client for this gate. VS-011 is expected to add prediction and reconciliation on top of the authoritative server behavior.
