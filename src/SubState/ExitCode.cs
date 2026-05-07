using System;
using UnityEngine;
namespace MornLib {
    [Serializable]
    public struct ExitCode : IEquatable<ExitCode> {
        [SerializeField] private string _value;
        public ExitCode(string value) { _value = value; }
        public string Value => _value;
        public bool IsEmpty => string.IsNullOrEmpty(_value);
        public static implicit operator string(ExitCode c) => c._value;
        public static implicit operator ExitCode(string s) => new ExitCode(s);
        public override string ToString() => _value ?? string.Empty;
        public bool Equals(ExitCode other) => _value == other._value;
        public override bool Equals(object obj) => obj is ExitCode other && Equals(other);
        public override int GetHashCode() => _value != null ? _value.GetHashCode() : 0;
        public static bool operator ==(ExitCode a, ExitCode b) => a._value == b._value;
        public static bool operator !=(ExitCode a, ExitCode b) => a._value != b._value;
    }
}
