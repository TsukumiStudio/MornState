# MornState

Unity 用の自前 FSM (State Machine)。Arbor の代替。
このドキュメントは **AI / 自動化エージェント向け**。GraphView GUI を使わずに状態遷移を読み書きする手順を中心に書かれている。

## 概念

| 型 | 役割 |
|---|---|
| `MornStateMachine` | MonoBehaviour。`StateNode` のリストを保持し、`Transition(stateID)` で現在状態を切り替える |
| `MornStateMachine.StateNode` | `int id` (一意な不変ID) / `string name` / `[SerializeReference] List<MornStateBehaviour> behaviours` |
| `MornStateBehaviour` | `[Serializable]` plain class。`OnStateBegin / OnStateUpdate / OnStateEnd` を override してロジックを書く。`Owner` から `gameObject` / `transform` / `destroyCancellationToken` / `CancellationTokenOnEnd` にアクセス |
| `Connection` | 遷移先参照。`int stateID` のみ持つ。`stateID == 0` は未接続。`Transition(connection)` で発火 |
| `ProcessBase : MornStateBehaviour` | `abstract float Progress` を持つ。`ProcessEnd` などが `Progress >= 1` で揃ったら遷移するパターン |

## 新しい State を作る

最小テンプレート:

```csharp
using System;
using MornLib;
using UnityEngine;

namespace YourNamespace
{
    [Serializable]                              // 必須: [SerializeReference] の警告回避
    public sealed class MyState : MornStateBehaviour
    {
        [SerializeField] private float _duration;
        [SerializeField] private Connection _onComplete;

        public override void OnStateBegin()
        {
            // 開始時の処理。Owner.gameObject や CancellationTokenOnEnd 利用可
        }

        public override void OnStateUpdate()
        {
            // 毎フレーム呼ばれる。遷移したいときは Transition(_onComplete);
        }

        public override void OnStateEnd()
        {
            // 終了時のクリーンアップ
        }
    }
}
```

ルール:
- **`[Serializable]` を必ず付ける** (継承されないので各派生に明示)
- 遷移先は `Connection` フィールドで宣言。GraphView でユーザがドラッグ接続するか、後述の編集 API で接続
- `Transition(_onComplete)` で `Connection` を発火 (内部的には `Owner.Transition(stateID)`)
- `MonoBehaviour` ではないので `Update()` などは呼ばれない。`OnStateUpdate` を override
- `gameObject.GetComponent<>()` 系は `Owner` 経由 (`Owner.GetComponentInChildren<>()` など)
- `[Inject]` (VContainer) は **field / method** で利用可能。`MornStateMachine` が container を解決して全 behaviour に inject 済み

## CLI から FSM を読み書きする (uloop 経由)

シーン上の `MornStateMachine` を AI が GraphView GUI なしで操作する手段。

### 構造ダンプ

```csharp
// uloop execute-dynamic-code に渡す snippet
return MornLib.MornStateExportUtil.ExportByName("StateMachine");
```

出力例:
```
FSM: StateMachine  (path: /StateMachine)
StartState: Title (id=1)
PlayOnStart: True
Nodes: 3

[Title] (id=1)
  - TransitionState
      _nextState -> Loading (id=2)

[Loading] (id=2)
  - WaitTimeState
      _duration = 2
      _nextState -> Game (id=3)

[Game] (id=3)
  - BeatPlayState
      _music = null
      _executeIsolated = False
      _onComplete -> Title (id=1)
```

Connection field は `field -> 解決された state 名 (id=N)` で表示。`(none)` は未接続。

### プログラマブル編集

```csharp
var fsm = MornLib.MornStateEditUtil.FindFsmByName("StateMachine");

// State 追加 (id は既存max+1で自動採番)
var loading = MornLib.MornStateEditUtil.AddState(fsm, "Loading");

// Behaviour 追加 (型名は短縮名 or full name)
MornLib.MornStateEditUtil.AddBehaviour(fsm, loading.id, "WaitTimeState");

// パラメータ設定
MornLib.MornStateEditUtil.SetField(fsm, loading.id, behaviourIndex: 0, "_duration", 2.0f);

// 接続
MornLib.MornStateEditUtil.Connect(
    fsm,
    fromStateID: 1,
    behaviourIndex: 0,
    fieldName: "_nextState",
    toStateID: loading.id
);

// 切断 (toStateID = 0)
MornLib.MornStateEditUtil.Disconnect(fsm, 1, 0, "_nextState");

// 削除 (state を消すと残った Connection は自動で 0 化)
MornLib.MornStateEditUtil.RemoveState(fsm, loading.id);
MornLib.MornStateEditUtil.RemoveBehaviour(fsm, stateID: 1, behaviourIndex: 0);
MornLib.MornStateEditUtil.RenameState(fsm, 1, "TitleScreen");

// 利用可能な MornStateBehaviour 派生型を一覧
var types = MornLib.MornStateEditUtil.ListAvailableBehaviourTypes();
```

仕様:
- 全 API は **Editor 専用**。`Undo.RegisterCompleteObjectUndo` 登録 + `EditorUtility.SetDirty` + `EditorSceneManager.MarkSceneDirty` 自動
- 引数エラーは `ArgumentException` で投げる (uloop の result に Error として現れる)
- `behaviourIndex` は state 内 behaviour リストの index (0始まり)
- `Connect` で `toStateID = 0` を指定すると `Disconnect` と等価

### A → Mid → B 割り込み

専用 helper はないので primitive を組み合わせる:

```csharp
var fsm = MornLib.MornStateEditUtil.FindFsmByName("StateMachine");
// 1. 元の接続先を取得 (Connection.stateID を reflection で読む)
var startNode = fsm.FindNode(fromStateID);
var connField = startNode.behaviours[0].GetType().GetField("_nextState",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
var oldTarget = ((MornLib.Connection)connField.GetValue(startNode.behaviours[0])).stateID;
// 2. 中間 state を追加 + 接続を張り替え
var mid = MornLib.MornStateEditUtil.AddState(fsm, "Mid");
MornLib.MornStateEditUtil.AddBehaviour(fsm, mid.id, "TransitionState");
MornLib.MornStateEditUtil.Connect(fsm, fromStateID, 0, "_nextState", mid.id);
MornLib.MornStateEditUtil.Connect(fsm, mid.id, 0, "_nextState", oldTarget);
```

## ライフサイクル

```
LifetimeScope.Awake          ← VContainer container build + auto-inject
   ↓
MornStateMachine.Construct  ← USE_VCONTAINER 時、全 behaviour に resolver.Inject(b)
   ↓
MornStateMachine.Awake      ← SetOwner / RebuildConnectionCache
   ↓
MornStateMachine.Start      ← _playOnStart なら Transition(_startStateID)
   ↓
[OnStateBegin → OnStateUpdate (毎フレーム) → OnStateEnd]
   ↓
Transition(connection) 呼出時に上記サイクル
```

注意点:
- 1 フレームに `MaxTransitionsPerFrame = 64` 回を超える Transition は無限ループとして検知し `Debug.LogError` で停止
- 同じ frame の `OnStateBegin` 内から `Transition` 連鎖した場合も上限内ならそのまま処理 (queue を while で flush)
- `CancellationTokenOnEnd` は state 終了時に自動 Cancel + Dispose
- `destroyCancellationToken` は Owner GameObject 破棄時に発火

## GraphView (人間用)

`Tools > MornState > Graph` または `Window > MornState Graph`。State の追加・削除・接続を GUI で。AI は通常上記 CLI API を使えばよい。

## ファイル構成

```
Assets/_Morn/MornState/
├── README.md                              ← この文書
└── src/
    ├── package.json                       UPM
    ├── MornState.asmdef                   Runtime + VContainer (versionDefine)
    ├── MornStateBehaviour.cs              基底
    ├── Connection.cs                      遷移先参照
    ├── MornStateMachine.cs                FSM 本体 (MonoBehaviour)
    ├── MornStateMachineInternal.cs        抽象基底
    ├── ProcessBase.cs                     Progress を持つ State
    ├── EmptyState.cs                      空 State (graph 表示用)
    ├── Samples/                           サンプル State
    └── Editor/
        ├── MornState.Editor.asmdef
        ├── MornStateMachineGraphView.cs   GraphView 本体
        ├── MornStateBehaviourPropertyDrawer.cs   全 behaviour 共通の PropertyDrawer
        ├── MornStateExportUtil.cs         CLI Export
        └── MornStateEditUtil.cs           CLI Edit
```

## 既知の制約

- `MornStateBehaviour` は `UnityEngine.Object` ではないので `[CustomEditor]` 不可。Inspector 拡張は `MornStateBehaviourPropertyDrawer` 経由 (PropertyDrawer + IMGUI で `[Button]` `[OnInspectorGUI]` を処理)
- `[SerializeReference]` の制約:
  - クラスを別 assembly に移動すると serialized data が壊れる
  - クラス rename は `[UnityEngine.Scripting.APIUpdating.MovedFrom]` で吸収可能
- `[Inject]` の constructor injection は不可 (deserialize 経由で生成されるため)。field / method injection のみ
