using System;
using Cysharp.Threading.Tasks;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("UI")]
    public class PopUpState : MornStateBehaviour
    {
        [Inject] private IObjectResolver _container;
        [SerializeField] private CanvasGroup _origin;
        [SerializeField] private Selectable _target;
        [SerializeField] private GameObject _prefab;
        [SerializeField, NoLabel] private MornStateUILayerType _layerType = new() { Key = "Main" };
        [SerializeField] private StateLink _onClosed;
        private GameObject _instance;
        private bool _waitClose;
        private bool _cachedIsInteractable;
        private bool _cachedBlocksRaycasts;

        public override void OnStateBegin()
        {
            if (_target == null || _prefab == null) return;
            _waitClose = false;
            _target.OnSubmitAsObservable().Subscribe(_ =>
            {
                if (_waitClose || _instance != null) return;
                _waitClose = true;
                _cachedIsInteractable = _origin.interactable;
                _cachedBlocksRaycasts = _origin.blocksRaycasts;
                _origin.interactable = false;
                _origin.blocksRaycasts = false;
                var parent = FindParent();
                if (parent == null)
                {
                    Debug.LogError($"[PopUpState] MornUIParent not found in scene. Layer: {_layerType}");
                    _waitClose = false;
                    _origin.interactable = _cachedIsInteractable;
                    _origin.blocksRaycasts = _cachedBlocksRaycasts;
                    return;
                }

                _instance = _container.Instantiate(_prefab, parent.transform);
            }).AddTo(CancellationTokenOnEnd);
        }

        private MornUIParent FindParent()
        {
            var parents = UnityEngine.Object.FindObjectsByType<MornUIParent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var parent in parents)
            {
                if (parent == null) continue;
                if (parent.LayerType != _layerType) continue;
                return parent;
            }

            return null;
        }

        public override void OnStateUpdate()
        {
            if (_waitClose && _instance == null)
            {
                _waitClose = false;
                _origin.interactable = _cachedIsInteractable;
                _origin.blocksRaycasts = _cachedBlocksRaycasts;
                Transition(_onClosed);
            }
        }

        public override void OnStateEnd()
        {
            if (_instance != null)
            {
                Object.Destroy(_instance);
                _instance = null;
            }
        }
    }
}
