using System;
using TMPro;
using UnityEngine;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Tween")]
    internal class TextTweenProcess : ProcessBase
    {
        [SerializeField] private TMP_Text _target;
        [SerializeField] private float _duration;
        [SerializeField] private StateLink _nextState;
        private float _startTime;
        private int _totalCharacters;
        public override float Progress => Mathf.Clamp01((Time.time - _startTime) / _duration);

        public override void OnStateBegin()
        {
            _startTime = Time.time;
            _target.ForceMeshUpdate();
            _totalCharacters = _target.textInfo.characterCount;
            _target.maxVisibleCharacters = 0;
        }

        public override void OnStateUpdate()
        {
            var visibleCount = Mathf.FloorToInt(Progress * _totalCharacters);
            _target.maxVisibleCharacters = visibleCount;

            if (Progress >= 1f)
            {
                _target.maxVisibleCharacters = _totalCharacters;
                Transition(_nextState);
            }
        }
    }
}
