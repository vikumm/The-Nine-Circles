using Divinity.GameRules.Movement;
using UnityEngine;

namespace Divinity.Client.Movement
{
    public sealed class DivinityPredictionBootstrap : MonoBehaviour
    {
        [SerializeField] private Transform playerVisual;
        [SerializeField] private float spawnX = 8f;
        [SerializeField] private float spawnY = 8f;

        private readonly ClientMovementPredictor _predictor = new ClientMovementPredictor(ClientPredictionSettings.Default);
        private ulong _nextSequence = 1;
        private ulong _clientTick;

        public PredictedMovementState State
        {
            get { return _predictor.State; }
        }

        private void Awake()
        {
            _predictor.Reset(spawnX, spawnY, CardinalDirection.South);
            ApplyRenderPosition();
        }

        private void Update()
        {
            var direction = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (direction.sqrMagnitude > 0f)
            {
                PredictDirection(direction.x, direction.y);
            }

            if (Input.GetMouseButtonDown(0) && Camera.main != null)
            {
                var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                PredictClickTarget(world.x, world.y);
            }
        }

        public void PredictDirection(float x, float y)
        {
            _predictor.ApplyLocalInput(PredictedMoveIntent.Direction(_nextSequence++, ++_clientTick, x, y));
            ApplyRenderPosition();
        }

        public void PredictClickTarget(float x, float y)
        {
            _predictor.ApplyLocalInput(PredictedMoveIntent.ClickTarget(_nextSequence++, ++_clientTick, x, y));
            ApplyRenderPosition();
        }

        public void ApplyAuthoritativeSnapshot(ulong snapshotId, ulong ackSequence, float x, float y)
        {
            _predictor.ApplySnapshot(new AuthoritativeMovementSnapshot(snapshotId, ackSequence, x, y));
            ApplyRenderPosition();
        }

        public void ApplyServerCorrection(ulong ackSequence, float x, float y)
        {
            _predictor.ApplyCorrection(ackSequence, x, y);
            ApplyRenderPosition();
        }

        private void ApplyRenderPosition()
        {
            if (playerVisual == null)
            {
                return;
            }

            var state = _predictor.State;
            playerVisual.position = new Vector3((float)state.RenderX, (float)state.RenderY, playerVisual.position.z);
        }
    }
}
