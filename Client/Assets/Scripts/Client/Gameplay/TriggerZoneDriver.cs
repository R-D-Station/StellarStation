using UnityEngine;
using Client.Gameplay.Camera;

namespace Client.Gameplay
{
    /// <summary>Тикает <see cref="TriggerZoneRegistry"/> позицией, за которой следует камера; поднимается сам, привязка в сцене не нужна.</summary>
    public sealed class TriggerZoneDriver : MonoBehaviour
    {
        private FollowCamera _camera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject(nameof(TriggerZoneDriver)) { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<TriggerZoneDriver>();
            DontDestroyOnLoad(go);
        }

        private void LateUpdate()
        {
            if (TriggerZoneRegistry.Count == 0)
                return;

            if (_camera == null)
                _camera = FindFirstObjectByType<FollowCamera>();

            Transform target = _camera != null ? _camera.Target : null;
            if (target == null)
                return;

            TriggerZoneRegistry.Evaluate(target.position);
        }
    }
}
