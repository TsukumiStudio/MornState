using UnityEngine;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal sealed class PlaySoundState : MornStateBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _audioClip;

        public override void OnStateBegin()
        {
            _audioSource.clip = _audioClip;
            _audioSource.Play();
        }
    }
}