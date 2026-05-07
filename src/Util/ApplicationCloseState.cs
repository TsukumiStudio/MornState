using UnityEditor;
using System;

namespace MornLib
{
    [Serializable]
    [MornStateMenu("Util")]
    internal class ApplicationCloseState : MornStateBehaviour
    {
        public override void OnStateBegin()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }
    }
}