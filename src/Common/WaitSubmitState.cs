using Cysharp.Threading.Tasks;
using System;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using UnityEngine.UI;

namespace MornLib
{
	[Serializable]
	[MornStateMenu("Common")]
	internal sealed class WaitSubmitState : MornStateBehaviour
	{
		[SerializeField] private Selectable _target;
		[SerializeField] private Connection _nextState;

		public override void OnStateBegin()
		{
			_target.OnSubmitAsObservable().Subscribe(_ => OnClick()).AddTo(CancellationTokenOnEnd);
		}

		private void OnClick()
		{
			Transition(_nextState);
		}
	}
}