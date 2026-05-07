#if USE_VIDEO
using System;
using UnityEngine;
using UnityEngine.Video;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Process")]
    internal class PlayRandomVideoProcess : ProcessBase
    {
        [SerializeField] private int _rareRate = 4;
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private VideoClip[] _rareClips;
        [SerializeField] private VideoClip[] _clips;
        public override float Progress
        {
            get
            {
                if (_videoPlayer)
                {
                    if (_videoPlayer.frame > 0 && !_videoPlayer.isPlaying)
                    {
                        return 1;
                    }

                    return Mathf.Clamp01(_videoPlayer.frame / (float)_videoPlayer.frameCount);
                }

                return 1f;
            }
        }

        public override void OnStateBegin()
        {
            var countSum = _rareClips.Length + _clips.Length * _rareRate;

            // レアは通常より_rareRate倍出にくい
            if (Random.Range(0, countSum) < _rareClips.Length)
            {
                _videoPlayer.clip = _rareClips[Random.Range(0, _rareClips.Length)];
            }
            else
            {
                _videoPlayer.clip = _clips[Random.Range(0, _clips.Length)];
            }

            _videoPlayer.Play();
        }
    }
}
#endif