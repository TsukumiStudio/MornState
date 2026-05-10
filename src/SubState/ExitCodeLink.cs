using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public sealed class ExitCodeLink {
        [SerializeField] private ExitCode _exitCode;
        [SerializeField] private StateLink _next;
        public ExitCode ExitCode { get => _exitCode; set => _exitCode = value; }
        public StateLink Next { get => _next; set => _next = value; }
    }
}
