using System;
namespace MornLib {
    public static class MornStateBehaviourExtensions {
        public static T AddTo<T>(this T disposable, MornStateBehaviour behaviour) where T : IDisposable {
            if(behaviour == null || behaviour.gameObject == null) {
                disposable?.Dispose();
                return disposable;
            }
            return UniRx.DisposableExtensions.AddTo(disposable, behaviour.gameObject);
        }
    }
}
