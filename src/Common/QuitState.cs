using System;
namespace MornLib
{
    [Serializable]
    [MornStateMenu("Common")]
    internal class QuitState : MornStateBehaviour
    {
        public override void OnStateBegin()
        {
            MornApp.Quit();
        }
    }
}