# Divinity.ContentBuilder

Minimal VS-004/VS-009 content pipeline.

Commands:

```bash
dotnet run --project tools/content-builder/Divinity.ContentBuilder.csproj --configuration Release -- build --content-root content --output-root tools/content-builder/artifacts
dotnet run --project tools/content-builder/Divinity.ContentBuilder.csproj --configuration Release -- validate --content-root content
```

The builder validates source content before writing artifacts:

- `unity/<mapId>.visual.json` for client-side rendering/bootstrap;
- `server/<mapId>.authoritative.json` for server-side authority.

Both artifacts include the same `contentHash`. The VS-009 artifacts include a generated 16x16 chunk grid for the 96x96 Training Field map. The client artifact is not an authority source.
