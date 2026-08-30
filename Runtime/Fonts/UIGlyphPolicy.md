# UI で使える記号（PolyLing）

## 前提

ランタイムビルドで日本語を出すため、UI テキストのフォントは同梱の
Noto Sans JP に固定してある。

- `Runtime/Fonts/NotoSansJP-VariableFont_wght.ttf` … 元フォント
- `Runtime/Fonts/NotoSansJP-VariableFont_wght SDF.asset` … TMP フォントアセット
  （`m_AtlasPopulationMode: 1` = Dynamic。不足字は実行時に TTF から追加される）
- `Runtime/Fonts/Panel Text Settings.asset` … `m_DefaultFontAsset` が上の SDF を指す

`m_FallbackFontAssetTable` と `m_FallbackFontAssets` はどちらも空にしてある。
**フォールバック先が無いので、Noto Sans JP に無い字は必ず豆腐になる。**

## 規則

UI に出す文字列（ボタン、ラベル、ツールチップ、Localization の辞書）には
Noto Sans JP に収録されている字だけを使うこと。とくに絵文字は使えない。

`m_MissingCharacterUnicode` は `12307`（U+3013 〓 ゲタ記号）にしてある。
UI に 〓 が出たら未収録字を使ったということなので、下の手順で確認して差し替える。

## 使える記号（収録確認済み）

```
▲ ▼ ▶ ◀        U+25B2 U+25BC U+25B6 U+25C0
★ ☆            U+2605 U+2606
■ □ ● ○ ◉ ◎    U+25A0 U+25A1 U+25CF U+25CB U+25C9 U+25CE
◆ ◇            U+25C6 U+25C7
─ │ ├ └ ┘ ┴    U+2500 U+2502 U+251C U+2514 U+2518 U+2534
→ ← ↑ ↓        U+2192 U+2190 U+2191 U+2193
↔ ⇔ ⇄ ⇆ ⇒      U+2194 U+21D4 U+21C4 U+21C6 U+21D2
… ※ • — – − ×  U+2026 U+203B U+2022 U+2014 U+2013 U+2212 U+00D7
① ② ③ ④        U+2460..U+2463
≥ ≤ ≦ ≠ ≈ ≡    U+2265 U+2264 U+2266 U+2260 U+2248 U+2261
∈ ∪ ∓ ⊙        U+2208 U+222A U+2213 U+2299
⚠ ✓ ♪ † § ±    U+26A0 U+2713 U+266A U+2020 U+00A7 U+00B1
```

## 使えない記号（豆腐になる。過去に混入していたもの）

```
絵文字全般      👁 U+1F441 / 🔒 U+1F512 / 🔓 U+1F513 / 🪞 U+1FA9E
Dingbats 一部   ✋ U+270B / ✔ U+2714 / ✗ U+2717 / ✕ U+2715 / ✎ U+270E
矢印 一部       ↳ U+21B3 / ↶ U+21B6 / ↷ U+21B7 / ⟲ U+27F2 / ⟺ U+27FA
図形 一部       ▸ U+25B8
その他          ≒ U+2252 / ⁻ U+207B
結合文字        U+0338（打ち消し線）など。合成して打ち消し記号は作れない
```

`✓ U+2713` は収録されているが `✔ U+2714` は無い。
`× U+00D7` は収録されているが `✕ U+2715` `✗ U+2717` は無い。
似た字形でもコードポイントが違えば結果が変わるので、必ず個別に確認すること。

## 確認手順

追加した字が収録されているかは、フォントの cmap を直接引いて確かめる。
目視や「たぶん入っている」で判断しないこと。

```bash
pip install fonttools
python3 - <<'EOF'
from fontTools.ttLib import TTFont
f = TTFont('Runtime/Fonts/NotoSansJP-VariableFont_wght.ttf', lazy=True)
cm = set()
for t in f['cmap'].tables: cm |= set(t.cmap.keys())
for ch in "◉■□※×⇆":                      # 調べたい字をここに並べる
    print(f"U+{ord(ch):04X} {ch} :", "OK" if ord(ch) in cm else "MISSING")
EOF
```

## 走査時の注意

文字はソース上に 2 通りの書き方で現れる。両方を見ること。

- リテラル … `"👁"`
- エスケープ … `"\u270B"` / `"\U0001FA9E"`

`\u` は 16 進 4 桁ちょうど、`\U` は 8 桁ちょうど。
`"\u21C6B"` は `⇆` + `B` であって U+21C6B ではない。

`'\uFEFF'`（BOM）のように表示しない用途で使うものは対象外。
`//` と `///` のコメント内も表示されないので対象外。

## 別解を採らなかった理由

- **フォールバックフォントの追加**
  絵文字フォントの同梱でパッケージが数 MB 増え、ライセンス表記も増える。
  カラー絵文字は UIToolkit の SDF 経路では単色になるため得るものが少ない。
- **USS の background-image によるアイコン化**
  画像アセットの新規作成とレイアウト変更が必要で影響範囲が広い。

将来アイコンを増やすなら 2 番目を検討する価値はあるが、
そのときも「テキストに絵文字を書く」形には戻さないこと。
