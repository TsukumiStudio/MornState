using System;
namespace MornLib
{
    [Serializable]
    [MornStateMenu("Action")]
    internal class DestroyOwnState : MornStateBehaviour
    {
        public override void OnStateBegin()
        {
            Destroy(gameObject);
        }
    }
}