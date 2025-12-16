# SimpleMeshFactory GPU描画 + カリング統合プラン

## 概要

現状のCPUベース描画（Handles.DrawLine等）をGPUベースに移行し、
同時に面の表裏判定に連動したエッジ・頂点カリングを実装する。

---

## 現状の問題点

### パフォーマンス問題

```
問題1: 毎フレーム重複計算
  - HashSetでエッジ抽出（O(n)のメモリ確保）
  - WorldToPreviewPosで毎頂点ごとに行列計算

問題2: 個別描画呼び出し
  - Handles.DrawLine() を数百回呼び出し
  - CPU→GPU間のドローコールが多い

問題3: カリングなし
  - 裏面のエッジ・頂点も全て描画
  - 複雑なメッシュで視認性が悪い
```

### 目標

| 項目 | 現状 | 目標 |
|------|------|------|
| 座標変換 | CPU (毎フレーム) | GPU (Compute Shader) |
| 描画 | Handles.DrawLine (個別) | DrawProcedural (一括) |
| カリング | なし | 面の表裏連動 |
| 対応頂点数 | ~1000 | ~100,000+ |

---

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────┐
│                        CPU (C#)                                 │
├─────────────────────────────────────────────────────────────────┤
│  MeshData変更時のみ:                                            │
│    - 頂点バッファ更新                                           │
│    - 線分バッファ生成（Face→辺の展開）                          │
│    - 面データバッファ更新                                        │
│                                                                 │
│  毎フレーム:                                                     │
│    - MVP行列をGPUに送信                                         │
│    - Compute Shader ディスパッチ                                │
│    - DrawProceduralIndirect 呼び出し                            │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    GPU (Compute Shader)                         │
├─────────────────────────────────────────────────────────────────┤
│  Pass 1: 頂点座標変換                                           │
│    入力: _PositionBuffer (ワールド座標)                         │
│    出力: _ScreenPositionBuffer (スクリーン座標 + 深度)          │
│                                                                 │
│  Pass 2: 面の表裏判定 + 可視性書き込み                          │
│    入力: _ScreenPositionBuffer, _FaceVertexIndexBuffer          │
│    出力: _VertexVisibilityBuffer (頂点の可視性)                 │
│           _FaceVisibilityBuffer (面の可視性)                    │
│                                                                 │
│  Pass 3: 線分の可視性計算                                       │
│    入力: _FaceVisibilityBuffer, _LineFaceIndexBuffer            │
│    出力: _LineVisibilityBuffer                                  │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    GPU (URP Shader)                             │
├─────────────────────────────────────────────────────────────────┤
│  点シェーダー:                                                   │
│    - インスタンシング（1頂点 = 1インスタンス）                   │
│    - Quad生成（4頂点/インスタンス）                             │
│    - 可視性チェック → 非表示ならdiscard                         │
│                                                                 │
│  線シェーダー:                                                   │
│    - インスタンシング（1線分 = 1インスタンス）                   │
│    - 太さ付きQuad生成（4頂点/インスタンス）                      │
│    - 可視性チェック → 非表示ならdiscard                         │
└─────────────────────────────────────────────────────────────────┘
```

---

## データ構造

### CPU側 (C#)

```csharp
/// <summary>
/// GPU描画用バッファ管理
/// </summary>
public class MeshGPUBuffers : IDisposable
{
    // === 入力バッファ（メッシュ変更時に更新） ===
    
    /// <summary>頂点ワールド座標</summary>
    public ComputeBuffer PositionBuffer;           // float3[]
    
    /// <summary>線分データ（面ごとに展開）</summary>
    public ComputeBuffer LineBuffer;               // LineData[]
    
    /// <summary>面の頂点インデックス（フラット配列）</summary>
    public ComputeBuffer FaceVertexIndexBuffer;    // int[]
    
    /// <summary>面ごとのオフセット</summary>
    public ComputeBuffer FaceVertexOffsetBuffer;   // int[]
    
    /// <summary>面ごとの頂点数</summary>
    public ComputeBuffer FaceVertexCountBuffer;    // int[]
    
    // === 出力バッファ（毎フレーム更新） ===
    
    /// <summary>スクリーン座標 + 深度</summary>
    public ComputeBuffer ScreenPositionBuffer;     // float3[]
    
    /// <summary>頂点の可視性</summary>
    public ComputeBuffer VertexVisibilityBuffer;   // float[]
    
    /// <summary>面の可視性</summary>
    public ComputeBuffer FaceVisibilityBuffer;     // float[]
    
    /// <summary>線分の可視性</summary>
    public ComputeBuffer LineVisibilityBuffer;     // float[]
    
    // === カウント ===
    public int VertexCount;
    public int FaceCount;
    public int LineCount;
}

/// <summary>
/// 線分データ（GPU用）
/// </summary>
public struct LineData
{
    public int V1;           // 始点の頂点インデックス
    public int V2;           // 終点の頂点インデックス
    public int FaceIndex;    // 属する面のインデックス
    public int LineType;     // 0=エッジ, 1=補助線(2頂点Face)
}
```

### GPU側 (HLSL)

```hlsl
// 入力バッファ
StructuredBuffer<float3> _PositionBuffer;
ByteAddressBuffer _FaceVertexIndexBuffer;
ByteAddressBuffer _FaceVertexOffsetBuffer;
ByteAddressBuffer _FaceVertexCountBuffer;
StructuredBuffer<LineData> _LineBuffer;

// 出力バッファ
RWStructuredBuffer<float3> _ScreenPositionBuffer;
RWStructuredBuffer<float> _VertexVisibilityBuffer;
RWStructuredBuffer<float> _FaceVisibilityBuffer;
RWStructuredBuffer<float> _LineVisibilityBuffer;

// 定数
float4x4 _MATRIX_MVP;
float4 _ScreenParams;  // (width, height, 1/width, 1/height)

struct LineData
{
    int v1;
    int v2;
    int faceIndex;
    int lineType;
};
```

---

## Compute Shader 設計

### Compute2D_GPU.compute

```hlsl
#pragma kernel ComputeScreenPositions
#pragma kernel ComputeFaceVisibility
#pragma kernel ComputeLineVisibility
#pragma kernel ClearVisibility

// ============================================================
// Pass 0: 可視性バッファをクリア
// ============================================================
[numthreads(64, 1, 1)]
void ClearVisibility(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index < _VertexCount)
    {
        _VertexVisibilityBuffer[index] = 0.0;
    }
}

// ============================================================
// Pass 1: 頂点座標をスクリーン座標に変換
// ============================================================
[numthreads(64, 1, 1)]
void ComputeScreenPositions(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= _VertexCount) return;
    
    float3 worldPos = _PositionBuffer[index];
    float4 clipPos = mul(_MATRIX_MVP, float4(worldPos, 1.0));
    
    // クリップ座標 → スクリーン座標
    float3 screenPos;
    if (clipPos.w > 0.0001)
    {
        float2 ndc = clipPos.xy / clipPos.w;
        screenPos.x = (ndc.x * 0.5 + 0.5) * _ScreenParams.x;
        screenPos.y = (1.0 - (ndc.y * 0.5 + 0.5)) * _ScreenParams.y;
        screenPos.z = clipPos.z / clipPos.w;  // 深度（近いほど大きい）
    }
    else
    {
        screenPos = float3(-10000, -10000, -10000);  // 画面外
    }
    
    _ScreenPositionBuffer[index] = screenPos;
}

// ============================================================
// Pass 2: 面の表裏判定 → 頂点に可視性を書き込み
// ============================================================
[numthreads(64, 1, 1)]
void ComputeFaceVisibility(uint3 id : SV_DispatchThreadID)
{
    uint faceIndex = id.x;
    if (faceIndex >= _FaceCount) return;
    
    int vertexCount = _FaceVertexCountBuffer.Load(faceIndex * 4);
    int offset = _FaceVertexOffsetBuffer.Load(faceIndex * 4);
    
    // 2頂点以下は常に表示
    if (vertexCount <= 2)
    {
        _FaceVisibilityBuffer[faceIndex] = 1.0;
        for (int j = 0; j < vertexCount; j++)
        {
            int vIdx = _FaceVertexIndexBuffer.Load((offset + j) * 4);
            _VertexVisibilityBuffer[vIdx] = 1.0;
        }
        return;
    }
    
    // スクリーン座標で時計回り判定
    float2 vertices[16];
    for (int i = 0; i < vertexCount && i < 16; i++)
    {
        int vIdx = _FaceVertexIndexBuffer.Load((offset + i) * 4);
        vertices[i] = _ScreenPositionBuffer[vIdx].xy;
    }
    
    bool isClockwise = IsClockwise(vertices, vertexCount);
    _FaceVisibilityBuffer[faceIndex] = isClockwise ? 1.0 : 0.0;
    
    // 表の面に属する頂点に可視性を書き込み
    if (isClockwise)
    {
        for (int k = 0; k < vertexCount; k++)
        {
            int vIdx = _FaceVertexIndexBuffer.Load((offset + k) * 4);
            _VertexVisibilityBuffer[vIdx] = 1.0;
        }
    }
}

// ============================================================
// Pass 3: 線分の可視性を面から取得
// ============================================================
[numthreads(64, 1, 1)]
void ComputeLineVisibility(uint3 id : SV_DispatchThreadID)
{
    uint lineIndex = id.x;
    if (lineIndex >= _LineCount) return;
    
    LineData line = _LineBuffer[lineIndex];
    
    // 補助線（2頂点Face）は常に表示
    if (line.lineType == 1)
    {
        _LineVisibilityBuffer[lineIndex] = 1.0;
        return;
    }
    
    // 通常エッジは属する面の可視性を参照
    _LineVisibilityBuffer[lineIndex] = _FaceVisibilityBuffer[line.faceIndex];
}

// ============================================================
// ヘルパー関数: 時計回り判定
// ============================================================
bool IsClockwise(float2 vertices[16], int numVertices)
{
    for (int i = 0; i < numVertices; i++)
    {
        int prev = (i + numVertices - 1) % numVertices;
        int next = (i + 1) % numVertices;
        
        float2 v1 = vertices[i] - vertices[prev];
        float2 v2 = vertices[next] - vertices[i];
        
        float cross = v1.x * v2.y - v1.y * v2.x;
        
        if (cross >= 0)
            return false;
    }
    return true;
}
```

---

## URP Shader 設計

### PointShader.shader

```hlsl
Shader "MeshFactory/Point"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 8.0
        _Color ("Color", Color) = (1,1,1,1)
        _SelectedColor ("Selected Color", Color) = (1,0.8,0,1)
    }
    
    SubShader
    {
        Tags { 
            "RenderType" = "Transparent" 
            "Queue" = "Overlay" 
            "RenderPipeline" = "UniversalPipeline" 
        }
        
        Pass
        {
            Name "Point"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // バッファ
            StructuredBuffer<float3> _ScreenPositionBuffer;
            StructuredBuffer<float> _VertexVisibilityBuffer;
            StructuredBuffer<uint> _SelectionBuffer;  // 選択状態
            
            float _PointSize;
            float4 _Color;
            float4 _SelectedColor;
            float2 _ScreenSize;
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float visibility : TEXCOORD1;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                
                uint pointIndex = input.instanceID;
                uint quadVertex = input.vertexID;  // 0-3
                
                // スクリーン座標を取得
                float3 screenPos = _ScreenPositionBuffer[pointIndex];
                float visibility = _VertexVisibilityBuffer[pointIndex];
                
                // Quadオフセット
                float2 offsets[4] = {
                    float2(-1, -1),
                    float2( 1, -1),
                    float2(-1,  1),
                    float2( 1,  1)
                };
                
                float2 pixelPos = screenPos.xy + offsets[quadVertex] * _PointSize * 0.5;
                
                // スクリーン座標 → クリップ座標
                o.positionCS.x = (pixelPos.x / _ScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (pixelPos.y / _ScreenSize.y) * 2.0;
                o.positionCS.z = 0.5;
                o.positionCS.w = 1.0;
                
                o.uv = offsets[quadVertex] * 0.5 + 0.5;
                o.visibility = visibility;
                
                // 選択状態で色分け
                uint isSelected = _SelectionBuffer[pointIndex];
                o.color = isSelected ? _SelectedColor : _Color;
                
                return o;
            }
            
            float4 frag(Varyings i) : SV_Target
            {
                // 非表示ならdiscard
                if (i.visibility < 0.5)
                    discard;
                
                // 円形にする
                float2 center = i.uv - 0.5;
                float dist = length(center);
                if (dist > 0.5)
                    discard;
                
                // エッジをソフトに
                float alpha = 1.0 - smoothstep(0.4, 0.5, dist);
                
                float4 col = i.color;
                col.a *= alpha;
                
                return col;
            }
            ENDHLSL
        }
    }
}
```

### LineShader.shader

```hlsl
Shader "MeshFactory/Line"
{
    Properties
    {
        _LineWidth ("Line Width", Float) = 2.0
        _EdgeColor ("Edge Color", Color) = (0,1,0.5,1)
        _AuxLineColor ("Aux Line Color", Color) = (1,0.3,1,1)
    }
    
    SubShader
    {
        Tags { 
            "RenderType" = "Transparent" 
            "Queue" = "Overlay" 
            "RenderPipeline" = "UniversalPipeline" 
        }
        
        Pass
        {
            Name "Line"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct LineData
            {
                int v1;
                int v2;
                int faceIndex;
                int lineType;
            };
            
            StructuredBuffer<float3> _ScreenPositionBuffer;
            StructuredBuffer<LineData> _LineBuffer;
            StructuredBuffer<float> _LineVisibilityBuffer;
            
            float _LineWidth;
            float4 _EdgeColor;
            float4 _AuxLineColor;
            float2 _ScreenSize;
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float visibility : TEXCOORD0;
                float2 lineUV : TEXCOORD1;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                
                uint lineIndex = input.instanceID;
                uint quadVertex = input.vertexID;  // 0-5 (2 triangles)
                
                LineData line = _LineBuffer[lineIndex];
                
                float3 p1 = _ScreenPositionBuffer[line.v1];
                float3 p2 = _ScreenPositionBuffer[line.v2];
                float visibility = _LineVisibilityBuffer[lineIndex];
                
                // 線分の方向と法線
                float2 dir = normalize(p2.xy - p1.xy);
                float2 normal = float2(-dir.y, dir.x);
                float2 offset = normal * _LineWidth * 0.5;
                
                // Quad頂点 (triangle strip: 0,1,2,3,4,5 → 2 triangles)
                float2 positions[6];
                positions[0] = p1.xy + offset;
                positions[1] = p1.xy - offset;
                positions[2] = p2.xy + offset;
                positions[3] = p2.xy + offset;
                positions[4] = p1.xy - offset;
                positions[5] = p2.xy - offset;
                
                float2 pixelPos = positions[quadVertex];
                
                // スクリーン座標 → クリップ座標
                o.positionCS.x = (pixelPos.x / _ScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (pixelPos.y / _ScreenSize.y) * 2.0;
                o.positionCS.z = 0.5;
                o.positionCS.w = 1.0;
                
                o.visibility = visibility;
                
                // 線種で色分け
                o.color = (line.lineType == 1) ? _AuxLineColor : _EdgeColor;
                
                // アンチエイリアス用
                float2 uvs[6] = {
                    float2(0, 1), float2(0, 0), float2(1, 1),
                    float2(1, 1), float2(0, 0), float2(1, 0)
                };
                o.lineUV = uvs[quadVertex];
                
                return o;
            }
            
            float4 frag(Varyings i) : SV_Target
            {
                if (i.visibility < 0.5)
                    discard;
                
                // エッジのアンチエイリアス
                float edgeDist = abs(i.lineUV.y - 0.5) * 2.0;
                float alpha = 1.0 - smoothstep(0.7, 1.0, edgeDist);
                
                float4 col = i.color;
                col.a *= alpha;
                
                return col;
            }
            ENDHLSL
        }
    }
}
```

---

## CPU側実装 (C#)

### MeshGPURenderer.cs

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    /// <summary>
    /// GPU描画管理クラス
    /// </summary>
    public class MeshGPURenderer : System.IDisposable
    {
        // === Compute Shader ===
        private ComputeShader _computeShader;
        private int _kernelClear;
        private int _kernelScreenPos;
        private int _kernelFaceVisibility;
        private int _kernelLineVisibility;
        
        // === 描画用マテリアル ===
        private Material _pointMaterial;
        private Material _lineMaterial;
        
        // === バッファ ===
        private MeshGPUBuffers _buffers;
        
        // === 状態 ===
        private bool _initialized;
        private int _lastMeshDataHash;
        
        // ============================================================
        // 初期化
        // ============================================================
        
        public void Initialize()
        {
            if (_initialized) return;
            
            // Compute Shader読み込み
            _computeShader = Resources.Load<ComputeShader>("Compute2D_GPU");
            _kernelClear = _computeShader.FindKernel("ClearVisibility");
            _kernelScreenPos = _computeShader.FindKernel("ComputeScreenPositions");
            _kernelFaceVisibility = _computeShader.FindKernel("ComputeFaceVisibility");
            _kernelLineVisibility = _computeShader.FindKernel("ComputeLineVisibility");
            
            // シェーダー読み込み
            _pointMaterial = new Material(Shader.Find("MeshFactory/Point"));
            _lineMaterial = new Material(Shader.Find("MeshFactory/Line"));
            
            _buffers = new MeshGPUBuffers();
            _initialized = true;
        }
        
        // ============================================================
        // バッファ更新（メッシュ変更時）
        // ============================================================
        
        public void UpdateBuffers(MeshData meshData)
        {
            if (meshData == null) return;
            
            int hash = meshData.GetHashCode();  // 簡易的なハッシュ
            if (hash == _lastMeshDataHash) return;
            _lastMeshDataHash = hash;
            
            // 既存バッファを解放
            _buffers?.Dispose();
            _buffers = new MeshGPUBuffers();
            
            // 頂点バッファ
            var positions = new Vector3[meshData.VertexCount];
            for (int i = 0; i < meshData.VertexCount; i++)
            {
                positions[i] = meshData.Vertices[i].Position;
            }
            _buffers.PositionBuffer = new ComputeBuffer(
                meshData.VertexCount, sizeof(float) * 3);
            _buffers.PositionBuffer.SetData(positions);
            _buffers.VertexCount = meshData.VertexCount;
            
            // 面データバッファ
            BuildFaceBuffers(meshData);
            
            // 線分バッファ（面から展開）
            BuildLineBuffers(meshData);
            
            // 出力バッファ
            _buffers.ScreenPositionBuffer = new ComputeBuffer(
                meshData.VertexCount, sizeof(float) * 3);
            _buffers.VertexVisibilityBuffer = new ComputeBuffer(
                meshData.VertexCount, sizeof(float));
            _buffers.FaceVisibilityBuffer = new ComputeBuffer(
                meshData.FaceCount, sizeof(float));
            _buffers.LineVisibilityBuffer = new ComputeBuffer(
                _buffers.LineCount, sizeof(float));
        }
        
        private void BuildFaceBuffers(MeshData meshData)
        {
            // フラット化した頂点インデックス
            var indices = new List<int>();
            var offsets = new int[meshData.FaceCount];
            var counts = new int[meshData.FaceCount];
            
            int offset = 0;
            for (int f = 0; f < meshData.FaceCount; f++)
            {
                var face = meshData.Faces[f];
                offsets[f] = offset;
                counts[f] = face.VertexCount;
                
                foreach (int vIdx in face.VertexIndices)
                {
                    indices.Add(vIdx);
                }
                offset += face.VertexCount;
            }
            
            _buffers.FaceVertexIndexBuffer = new ComputeBuffer(
                indices.Count, sizeof(int));
            _buffers.FaceVertexIndexBuffer.SetData(indices.ToArray());
            
            _buffers.FaceVertexOffsetBuffer = new ComputeBuffer(
                meshData.FaceCount, sizeof(int));
            _buffers.FaceVertexOffsetBuffer.SetData(offsets);
            
            _buffers.FaceVertexCountBuffer = new ComputeBuffer(
                meshData.FaceCount, sizeof(int));
            _buffers.FaceVertexCountBuffer.SetData(counts);
            
            _buffers.FaceCount = meshData.FaceCount;
        }
        
        private void BuildLineBuffers(MeshData meshData)
        {
            var lines = new List<LineData>();
            
            for (int f = 0; f < meshData.FaceCount; f++)
            {
                var face = meshData.Faces[f];
                
                if (face.VertexCount == 2)
                {
                    // 補助線
                    lines.Add(new LineData
                    {
                        V1 = face.VertexIndices[0],
                        V2 = face.VertexIndices[1],
                        FaceIndex = f,
                        LineType = 1  // 補助線
                    });
                }
                else if (face.VertexCount >= 3)
                {
                    // 各エッジを追加
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int v1 = face.VertexIndices[i];
                        int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                        
                        lines.Add(new LineData
                        {
                            V1 = v1,
                            V2 = v2,
                            FaceIndex = f,
                            LineType = 0  // 通常エッジ
                        });
                    }
                }
            }
            
            _buffers.LineBuffer = new ComputeBuffer(
                lines.Count, System.Runtime.InteropServices.Marshal.SizeOf<LineData>());
            _buffers.LineBuffer.SetData(lines.ToArray());
            _buffers.LineCount = lines.Count;
        }
        
        // ============================================================
        // 描画（毎フレーム）
        // ============================================================
        
        public void Render(Rect previewRect, Matrix4x4 mvp, HashSet<int> selectedVertices)
        {
            if (!_initialized || _buffers == null) return;
            if (_buffers.VertexCount == 0) return;
            
            // Compute Shader実行
            DispatchCompute(mvp, previewRect);
            
            // 選択状態バッファ更新
            UpdateSelectionBuffer(selectedVertices);
            
            // 描画
            DrawPoints(previewRect);
            DrawLines(previewRect);
        }
        
        private void DispatchCompute(Matrix4x4 mvp, Rect previewRect)
        {
            // 共通パラメータ
            _computeShader.SetMatrix("_MATRIX_MVP", mvp);
            _computeShader.SetVector("_ScreenParams", 
                new Vector4(previewRect.width, previewRect.height, 0, 0));
            _computeShader.SetInt("_VertexCount", _buffers.VertexCount);
            _computeShader.SetInt("_FaceCount", _buffers.FaceCount);
            _computeShader.SetInt("_LineCount", _buffers.LineCount);
            
            // Pass 0: クリア
            _computeShader.SetBuffer(_kernelClear, 
                "_VertexVisibilityBuffer", _buffers.VertexVisibilityBuffer);
            _computeShader.Dispatch(_kernelClear, 
                Mathf.CeilToInt(_buffers.VertexCount / 64f), 1, 1);
            
            // Pass 1: スクリーン座標計算
            _computeShader.SetBuffer(_kernelScreenPos, 
                "_PositionBuffer", _buffers.PositionBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, 
                "_ScreenPositionBuffer", _buffers.ScreenPositionBuffer);
            _computeShader.Dispatch(_kernelScreenPos, 
                Mathf.CeilToInt(_buffers.VertexCount / 64f), 1, 1);
            
            // Pass 2: 面の表裏判定
            _computeShader.SetBuffer(_kernelFaceVisibility, 
                "_ScreenPositionBuffer", _buffers.ScreenPositionBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, 
                "_FaceVertexIndexBuffer", _buffers.FaceVertexIndexBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, 
                "_FaceVertexOffsetBuffer", _buffers.FaceVertexOffsetBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, 
                "_FaceVertexCountBuffer", _buffers.FaceVertexCountBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, 
                "_VertexVisibilityBuffer", _buffers.VertexVisibilityBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, 
                "_FaceVisibilityBuffer", _buffers.FaceVisibilityBuffer);
            _computeShader.Dispatch(_kernelFaceVisibility, 
                Mathf.CeilToInt(_buffers.FaceCount / 64f), 1, 1);
            
            // Pass 3: 線分の可視性
            _computeShader.SetBuffer(_kernelLineVisibility, 
                "_LineBuffer", _buffers.LineBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, 
                "_FaceVisibilityBuffer", _buffers.FaceVisibilityBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, 
                "_LineVisibilityBuffer", _buffers.LineVisibilityBuffer);
            _computeShader.Dispatch(_kernelLineVisibility, 
                Mathf.CeilToInt(_buffers.LineCount / 64f), 1, 1);
        }
        
        private void DrawPoints(Rect previewRect)
        {
            _pointMaterial.SetBuffer("_ScreenPositionBuffer", 
                _buffers.ScreenPositionBuffer);
            _pointMaterial.SetBuffer("_VertexVisibilityBuffer", 
                _buffers.VertexVisibilityBuffer);
            _pointMaterial.SetBuffer("_SelectionBuffer", 
                _buffers.SelectionBuffer);
            _pointMaterial.SetVector("_ScreenSize", 
                new Vector2(previewRect.width, previewRect.height));
            
            // 1頂点 = 1インスタンス、4頂点/インスタンス
            Graphics.DrawProcedural(
                _pointMaterial,
                new Bounds(Vector3.zero, Vector3.one * 1000),
                MeshTopology.TriangleStrip,
                4,  // 頂点数/インスタンス
                _buffers.VertexCount  // インスタンス数
            );
        }
        
        private void DrawLines(Rect previewRect)
        {
            _lineMaterial.SetBuffer("_ScreenPositionBuffer", 
                _buffers.ScreenPositionBuffer);
            _lineMaterial.SetBuffer("_LineBuffer", 
                _buffers.LineBuffer);
            _lineMaterial.SetBuffer("_LineVisibilityBuffer", 
                _buffers.LineVisibilityBuffer);
            _lineMaterial.SetVector("_ScreenSize", 
                new Vector2(previewRect.width, previewRect.height));
            
            // 1線分 = 1インスタンス、6頂点/インスタンス
            Graphics.DrawProcedural(
                _lineMaterial,
                new Bounds(Vector3.zero, Vector3.one * 1000),
                MeshTopology.Triangles,
                6,  // 頂点数/インスタンス
                _buffers.LineCount  // インスタンス数
            );
        }
        
        // ============================================================
        // クリーンアップ
        // ============================================================
        
        public void Dispose()
        {
            _buffers?.Dispose();
            
            if (_pointMaterial != null)
                Object.DestroyImmediate(_pointMaterial);
            if (_lineMaterial != null)
                Object.DestroyImmediate(_lineMaterial);
        }
    }
    
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LineData
    {
        public int V1;
        public int V2;
        public int FaceIndex;
        public int LineType;
    }
}
```

---

## 実装フェーズ

### Phase 2.1: 基盤構築（1-2日）

```
□ MeshGPUBuffers クラス作成
□ Compute2D_GPU.compute 作成
  - ComputeScreenPositions カーネル
  - ClearVisibility カーネル
□ MeshGPURenderer 基本構造
□ 単体テスト（座標変換の検証）
```

### Phase 2.2: カリング実装（1-2日）

```
□ ComputeFaceVisibility カーネル
□ ComputeLineVisibility カーネル
□ 線分バッファ生成ロジック
□ カリング動作検証
```

### Phase 2.3: URPシェーダー（1-2日）

```
□ PointShader.shader
□ LineShader.shader
□ DrawProcedural 統合
□ 選択状態の色分け対応
```

### Phase 2.4: SimpleMeshFactory統合（1日）

```
□ DrawVertexHandles() 置き換え
□ DrawWireframeOverlay() 置き換え
□ フォールバック（GPU非対応時）
□ パフォーマンス計測・比較
```

### Phase 2.5: 拡張機能（オプション）

```
□ 選択エッジ/面のハイライト表示
□ ミラー描画のGPU対応
□ LOD（距離に応じた描画調整）
□ 深度テスト対応（オプション）
```

---

## ファイル構成

```
Assets/
├── Editor/
│   └── MeshFactory/
│       └── Rendering/
│           ├── MeshGPURenderer.cs
│           ├── MeshGPUBuffers.cs
│           └── LineData.cs
│
├── Resources/
│   └── MeshFactory/
│       └── Compute2D_GPU.compute
│
└── Shaders/
    └── MeshFactory/
        ├── PointShader.shader
        └── LineShader.shader
```

---

## 注意事項

### エディタ vs ランタイム

```
現状: エディタ専用（PreviewRenderUtility内）
将来: ランタイム対応も視野に

PreviewRenderUtility内でDrawProceduralを使う場合:
- Camera.current が必要
- OnPostRender または CommandBuffer で描画
```

### バッファ管理

```
重要: ComputeBuffer は明示的に Release() が必要

タイミング:
- メッシュ変更時: 再構築
- ウィンドウ閉じ時: 解放
- アセンブリリロード時: 解放
```

### 既存機能との共存

```
段階的移行:
1. GPU描画を追加（既存と並行）
2. 動作確認後、既存コードを削除
3. フォールバック（GPU非対応環境用）は残す
```

---

## 期待される効果

| 項目 | Before | After |
|------|--------|-------|
| 頂点1000個の描画 | ~5ms | <0.5ms |
| 頂点10000個の描画 | ~50ms | <1ms |
| エッジカリング | なし | あり |
| 頂点カリング | なし | あり |
| ドローコール | ~2000 | 2 |

---

## 参考リンク

- [Unity Compute Shader](https://docs.unity3d.com/Manual/class-ComputeShader.html)
- [Graphics.DrawProcedural](https://docs.unity3d.com/ScriptReference/Graphics.DrawProcedural.html)
- [URP Shader Guide](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/writing-shaders-urp.html)
