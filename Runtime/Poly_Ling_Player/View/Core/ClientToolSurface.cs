// ClientToolSurface.cs
// クライアント（別プロセスのパネル）側の IToolSurface（操作経路統一計画.md E、M-2）。
//
// 設定・操作は SetToolParamCommand／InvokeToolActionCommand をコマンドとしてホストへ送る
// （ホスト側 HostToolSurface と同じコマンド。担当者判定は受け口で掛かる）。
//
// 【読み取りは未対応】
//   ツールの値・概要の読み取りは、ホストへ問い合わせる経路（照会結果の受け取り）がまだ無い。
//   TryGet は常に false、GetGroup は空を返す。読み取りが要るパネルをクライアントへ出す段で扱う。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public sealed class ClientToolSurface : IToolSurface
    {
        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        private readonly Action<PanelCommand> _send;
        private readonly Func<int>            _modelIndex;

        public ClientToolSurface(Action<PanelCommand> send, Func<int> modelIndex)
        {
            _send       = send;
            _modelIndex = modelIndex;
        }

        public bool TryGet(string toolId, string name, out string value)
        {
            value = null;
            return false;
        }

        public bool TrySet(string toolId, string name, string value, out string actual)
        {
            actual = null;
            if (_send == null) return false;
            _send(new SetToolParamCommand(_modelIndex?.Invoke() ?? 0, toolId, name, value));
            actual = value;
            return true;
        }

        public bool Invoke(string toolId, string action) => InvokeWith(toolId, action, null, null);

        public bool InvokeWith(string toolId, string action, string[] argKeys, string[] argValues)
        {
            if (_send == null) return false;
            _send(new InvokeToolActionCommand(_modelIndex?.Invoke() ?? 0, toolId, action, argKeys, argValues));
            return true;
        }

        public IReadOnlyDictionary<string, string> GetGroup(string toolId, string group) => Empty;
    }
}
