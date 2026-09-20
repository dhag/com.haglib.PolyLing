// HostToolSurface.cs
// 本体（ホスト）側の IToolSurface（操作経路統一計画.md E-2）。
//
// 読み取りは登録簿のハンドラから PLToolSurface で直接読む。
// 設定・操作は SetToolParamCommand／InvokeToolActionCommand をホストの操作として送る。
// MCP・リモートと同じ経路にそろえ、操作者の記録と担当者判定を掛けるため。

using System;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public sealed class HostToolSurface : IToolSurface
    {
        private readonly Func<string, object>              _resolve;
        private readonly Func<PanelCommand, CommandResult> _dispatch;
        private readonly Func<int>                         _modelIndex;

        public HostToolSurface(
            Func<string, object> resolve,
            Func<PanelCommand, CommandResult> dispatch,
            Func<int> modelIndex)
        {
            _resolve    = resolve;
            _dispatch   = dispatch;
            _modelIndex = modelIndex;
        }

        public bool TryGet(string toolId, string name, out string value)
        {
            value = null;
            var h = _resolve?.Invoke(toolId);
            return h != null && PLToolSurface.TryGetValue(h, name, out value);
        }

        public bool TrySet(string toolId, string name, string value, out string actual)
        {
            actual = null;
            var r = _dispatch?.Invoke(new SetToolParamCommand(_modelIndex?.Invoke() ?? 0, toolId, name, value));
            if (r == null || !r.Success) return false;
            return TryGet(toolId, name, out actual);
        }

        public bool Invoke(string toolId, string action) => InvokeWith(toolId, action, null, null);

        public bool InvokeWith(string toolId, string action, string[] argKeys, string[] argValues)
        {
            var r = _dispatch?.Invoke(new InvokeToolActionCommand(
                _modelIndex?.Invoke() ?? 0, toolId, action, argKeys, argValues));
            return r != null && r.Success;
        }

        public System.Collections.Generic.IReadOnlyDictionary<string, string> GetGroup(string toolId, string group)
            => PLToolSurface.ReadGroup(_resolve?.Invoke(toolId), group);
    }
}
