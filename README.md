# MornState

Arbor 互換シグネチャを保ちつつ、UniTask 連携の軽量 FSM を提供するコアライブラリ。

## 公開 API (`namespace MornLib`)

| 型 | 役割 |
| --- | --- |
| `StateBehaviour : MonoBehaviour` | 全 State の基底。`OnStateBegin / OnStateUpdate / OnStateEnd` virtual + `Transition(StateLink)` |
| `StateLink` | `[SerializeField]` 可能な遷移先参照 (`stateID`, `transitionTiming`, `lineColor`, `name`, `transitionCount`) |
| `MornStateMachine` | StateBehaviour 群を子に持つ FSM コンポーネント。Update で current state を駆動 |
| `MornStateMachineInternal` | SubState がネスト参照する抽象基底 |
| `ProcessBase : StateBehaviour` | `abstract float Progress` を持つ Process 実装基底 |

## Editor

`Tools > MornState > Graph` で簡易 GraphView。選択中の `MornStateMachine` を表示し、ノード間ドラッグで `StateLink.stateID` を編集可能。

## 互換性

`using Arbor;` を `using MornLib;` に差し替えることを想定して API シグネチャを揃えている (StateBehaviour / StateLink / Transition / OnStateBegin/Update/End / CancellationTokenOnEnd / RebuildStateLinkCache / ProcessBase)。`ArborFSM` / `ArborFSMInternal` 相当は `MornStateMachine` / `MornStateMachineInternal` にリネーム。
