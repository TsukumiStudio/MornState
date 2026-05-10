using System;
using Cysharp.Threading.Tasks;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MornLib
{
    /// <summary>
    /// 指定UIBehaviourのポインターイベントごとに遷移するState。
    /// 不要な遷移先は未設定のままでよい。
    /// </summary>
    [Serializable]
    [MornStateMenu("UI")]
    internal sealed class OnPointerEventState : MornStateBehaviour
    {
        [SerializeField] private UIBehaviour _target;
        [SerializeField] private StateLink _onPointerEnter;
        [SerializeField] private StateLink _onPointerExit;
        [SerializeField] private StateLink _onPointerDown;
        [SerializeField] private StateLink _onPointerUp;
        [SerializeField] private StateLink _onPointerClick;

        public override void OnStateBegin()
        {
            _target.OnPointerEnterAsObservable()
                .Subscribe(_ => Transition(_onPointerEnter))
                .AddTo(CancellationTokenOnEnd);
            _target.OnPointerExitAsObservable()
                .Subscribe(_ => Transition(_onPointerExit))
                .AddTo(CancellationTokenOnEnd);
            _target.OnPointerDownAsObservable()
                .Subscribe(_ => Transition(_onPointerDown))
                .AddTo(CancellationTokenOnEnd);
            _target.OnPointerUpAsObservable()
                .Subscribe(_ => Transition(_onPointerUp))
                .AddTo(CancellationTokenOnEnd);
            _target.OnPointerClickAsObservable()
                .Subscribe(_ => Transition(_onPointerClick))
                .AddTo(CancellationTokenOnEnd);
        }
    }
}
