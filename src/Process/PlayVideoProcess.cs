#if USE_VIDEO
using System;
using UnityEngine;
using UnityEngine.Video;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Process")]
    internal class PlayVideoProcess : ProcessBase
    {
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private Connection _nextStateLink;
        public override float Progress => _videoPlayer ? Mathf.Clamp01(_videoPlayer.frame / (float)_videoPlayer.frameCount) : 1f;

        public override void OnStateBegin()
        {
            _videoPlayer.Play();
        }
    }
}
#endif