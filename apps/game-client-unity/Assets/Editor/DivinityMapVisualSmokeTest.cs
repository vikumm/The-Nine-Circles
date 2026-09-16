#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Divinity.Editor
{
    public static class DivinityMapVisualSmokeTest
    {
        public static void Run()
        {
            var artifactPath = Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "DivinityContent",
                "training-field-01.visual.json");

            if (!File.Exists(artifactPath))
            {
                Fail("Training Field visual artifact was not found: " + artifactPath);
            }

            var json = File.ReadAllText(artifactPath);
            var artifact = JsonUtility.FromJson<ClientContentArtifact>(json);

            Require(artifact != null, "visual artifact could not be parsed");
            Require(!string.IsNullOrWhiteSpace(artifact.contentHash), "contentHash is required");
            Require(artifact.map != null, "map is required");
            Require(artifact.map.bounds != null, "map bounds are required");
            Require(artifact.map.bounds.width == 96 && artifact.map.bounds.height == 96, "map bounds must be 96x96");
            Require(artifact.map.chunks != null, "map chunks are required");
            Require(artifact.map.chunks.chunkSize == 16, "chunk size must be 16");
            Require(artifact.map.chunks.columns == 6 && artifact.map.chunks.rows == 6, "chunk grid must be 6x6");
            Require(artifact.map.chunks.chunks != null && artifact.map.chunks.chunks.Length == 36, "chunk grid must contain 36 chunks");
            Require(artifact.map.safeSpawns != null && artifact.map.safeSpawns.Length > 0, "safe spawn is required");

            var root = new GameObject("VS-009 Training Field Visual Smoke");
            root.AddComponent<DivinityMapVisualSmokeMarker>();

            Debug.Log("VS-009 Training Field visual artifact loaded: " + artifact.contentHash);
            EditorApplication.Exit(0);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                Fail(message);
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError("VS-009 map visual smoke failed: " + message);
            EditorApplication.Exit(1);
            throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class ClientContentArtifact
        {
            public int schemaVersion;
            public string contentVersion = string.Empty;
            public string contentHash = string.Empty;
            public ClientMapArtifact? map;
        }

        [Serializable]
        private sealed class ClientMapArtifact
        {
            public string mapId = string.Empty;
            public string stableId = string.Empty;
            public string name = string.Empty;
            public GridBounds? bounds;
            public decimal tileSize;
            public GridCell[] blockedCells = Array.Empty<GridCell>();
            public MapRegion[] regions = Array.Empty<MapRegion>();
            public SafeSpawn[] safeSpawns = Array.Empty<SafeSpawn>();
            public MapTrigger[] triggers = Array.Empty<MapTrigger>();
            public MapChunkGrid? chunks;
        }

        [Serializable]
        private sealed class GridBounds
        {
            public int width;
            public int height;
        }

        [Serializable]
        private sealed class GridCell
        {
            public int x;
            public int y;
        }

        [Serializable]
        private sealed class GridRect
        {
            public int x;
            public int y;
            public int width;
            public int height;
        }

        [Serializable]
        private sealed class MapRegion
        {
            public string id = string.Empty;
            public string kind = string.Empty;
            public GridRect? bounds;
        }

        [Serializable]
        private sealed class SafeSpawn
        {
            public string id = string.Empty;
            public int x;
            public int y;
        }

        [Serializable]
        private sealed class MapTrigger
        {
            public string id = string.Empty;
            public string kind = string.Empty;
            public GridRect? bounds;
        }

        [Serializable]
        private sealed class MapChunkGrid
        {
            public int chunkSize;
            public int columns;
            public int rows;
            public MapChunk[] chunks = Array.Empty<MapChunk>();
        }

        [Serializable]
        private sealed class MapChunk
        {
            public int x;
            public int y;
            public int width;
            public int height;
            public GridCell[] blockedCells = Array.Empty<GridCell>();
        }
    }

    public sealed class DivinityMapVisualSmokeMarker : MonoBehaviour
    {
    }
}
#endif
