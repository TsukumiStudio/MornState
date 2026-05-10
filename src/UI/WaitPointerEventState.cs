using System;
using Cysharp.Threading.Tasks;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("UI")]
    internal class WaitPointerEventState : MornStateBehaviour
    {
        [SerializeField] private UIBehaviour _target;
        [SerializeField] private PointerEventType _pointerEventType;
        [SerializeField] private StateLink _nextState;

        public override void OnStateBegin()
        {
            var observable = _pointerEventType switch
            {
                PointerEventType.PointerDown  => _target.OnPointerDownAsObservable(),
                PointerEventType.PointerUp    => _target.OnPointerUpAsObservable(),
                PointerEventType.PointerEnter => _target.OnPointerEnterAsObservable(),
                PointerEventType.PointerExit  => _target.OnPointerExitAsObservable(),
                PointerEventType.PointerClick => _target.OnPointerClickAsObservable(),
                _                             => throw new ArgumentOutOfRangeException()
            };
            observable.Subscribe(_ => Transition(_nextState)).AddTo(CancellationTokenOnEnd);
        }

        private enum PointerEventType
        {
            PointerDown,
            PointerUp,
            PointerEnter,
            PointerExit,
            PointerClick
        }
    }
}
