using System;
using UnityEngine;

namespace MornLib
{
    /// <summary>現在のアニメーションが終了するまで待機するState</summary>
    [Serializable]
    [MornStateMenu("Animation")]
    internal sealed class WaitAnimationCompleteState : MornStateBehaviour
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private int _layer;
        [SerializeField] private Connection _onComplete;

        public override void OnStateUpdate()
        {
            // 遷移中は待機
            if (_animator.IsInTransition(_layer))
            {
                return;
            }

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(_layer);
            // normalizedTimeが1以上 かつ ループアニメーションでない場合は終了とみなす
            if (stateInfo.normalizedTime >= 1.0f && !stateInfo.loop)
            {
                Transition(_onComplete);
            }
        }
    }
}