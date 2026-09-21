// ModelContext.Revision.cs
// モデルの版。書き込みのあるコマンドが成功するたびに 1 つ進む。
// Runtime/Poly_Ling_Main/Core/Context/ に配置
//
// 【何に使うか】
//   MCP の呼び出し側が「前に見たときから変わったか」を 1 つの数で判定できるようにする。
//   変わっていなければ照会を省ける。版は保存しない（実行のたびに 0 から数え直す）。
//
// 【誰が進めるか】
//   PlayerCommandDispatcher が一番外側の呼び出しの最後に 1 回だけ進める
//   （PlayerCommandDispatcher.Revision.cs）。入れ子の呼び出しでは進めない。
//   ここから勝手に進めないこと。1 操作で何度も進むと「変わったか」の判定に使えない。

namespace Poly_Ling.Context
{
    public partial class ModelContext
    {
        /// <summary>モデルの版。0 は 1 度も書き換えていない。</summary>
        public int Revision { get; private set; }

        /// <summary>版を 1 つ進める。ディスパッチャだけが呼ぶ。</summary>
        public void BumpRevision()
        {
            unchecked { Revision++; }
        }
    }
}
