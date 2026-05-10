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
        [SerializeField] private CanvasGroup _origin;
        [SerializeField] private Selectable _target;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private StateLink _onClosed;
        [Inject] private IObjectResolver _container;
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
                _instance = _container.Instantiate(_prefab, _origin.transform.parent);
            }).AddTo(CancellationTokenOnEnd);
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