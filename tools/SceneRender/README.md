# SceneRender — Wallpaper Engine `scene.pkg` 静态高分辨率渲染工具

独立 .NET 10 控制台工具（`net10.0-windows`，无 WPF 依赖），把一个 Wallpaper Engine 场景壁纸的
`scene.pkg` **离线渲染成一张 PNG/JPEG**，用于给 WPF 程序当背景图。

- 工具目录：`F:\订单管理\tools\SceneRender\`
- 依赖 NuGet：`SixLabors.ImageSharp`（PNG/JPEG/內嵌图片解码 + 缩放 + 编码）、`K4os.Compression.LZ4`（TEX 的 LZ4 块）
- 外部工具：`ffmpeg`（可选，仅“视频贴图”需要；用于从 TEX 内嵌的 MP4 里抽一帧）
- 本 README 里的结论全部来自对**本机 48 个场景壁纸**的实测与 hexdump，不是猜测。

---

## 0. 结论速览

- 48 个含 `scene.pkg` 的场景壁纸**全部能解析出条目表、全部能出图**（48/48 无异常退出，全量 660x430 试渲共 49.6s，平均 1.03s/个）。
- 其中约 40 个画面视觉正常；剩下 8 个左右的缺陷有明确的格式原因（三维模型场景、引擎内置合成层缺失、粒子/全屏 shader 为主等），见 §5。
- 3 个目标壁纸（另加 1 个补充）已产出 **1320x860 / 2560x1440** 的 PNG，清晰度远高于 1079px 封面：

| 壁纸 ID | 输出文件 | 尺寸 | 文件大小 | 渲染耗时 | 峰值内存 |
|---|---|---|---|---|---|
| 3025975631 | `_out\final\3025975631_1320x860.png` | 1320x860 | 842 KB | 1.9 s | 298 MB |
| 3025975631 | `_out\final\3025975631_2560x1440.png` | 2560x1440 | 1.9 MB | 1.9 s | 311 MB |
| 3470764447 | `_out\final\3470764447_1320x860.png` | 1320x860 | 1.1 MB | 2.5 s | 468 MB |
| 3470764447 | `_out\final\3470764447_2560x1440.png` | 2560x1440 | 2.7 MB | 3.0 s | 482 MB |
| 2992022633 | `_out\final\2992022633_1320x860.png` | 1320x860 | 788 KB | 1.2 s | 228 MB |
| 2992022633 | `_out\final\2992022633_2560x1440.png` | 2560x1440 | 1.7 MB | 1.5 s | 242 MB |
| 3653963863（补充） | `_out\final\3653963863_1320x860.png` | 1320x860 | 1.5 MB | 0.8 s | 54 MB |

失败/受限样例：`_out\2887099508_1320x860.png`（三维模型场景，见 §5）。

（`_out\survey\` 下有全部 48 个场景 660x430 的试渲图、`montage.png` 联系表和 `survey.csv` 明细。）

---

## 1. 用法

```powershell
cd F:\订单管理\tools\SceneRender
dotnet build -c Release
$exe = 'bin\Release\net10.0-windows\SceneRender.dll'

# 1) 列条目表
dotnet $exe list 'F:\steam\steamapps\workshop\content\431960\3025975631\scene.pkg'

# 2) 解码一张贴图（包内条目 或 直接给 .tex 文件）
dotnet $exe tex 'F:\steam\...\3025975631\scene.pkg' 'materials/窗边的伊蕾娜 昼夜变化.tex' out.png --time=1.0
dotnet $exe tex 'F:\...\materials\赞助.tex' out.png

# 3) 渲整张场景（cover 裁切，任意宽高）
dotnet $exe render 'F:\steam\...\3470764447\scene.pkg' 1320 860 out.png
dotnet $exe render 'F:\steam\...\3470764447\scene.pkg' 2560 1440 out.png
dotnet $exe render 'F:\steam\...\3470764447\scene.pkg' 1320 860 out.jpg   # 自动按扩展名编码 JPEG

# 4) 其它
dotnet $exe info   '...\scene.pkg'                       # 打印画布/对象/层解析结果
dotnet $exe dump   '...\scene.pkg' 'scene.json' scene.json  # 导出任意包内条目
dotnet $exe main   '...\scene.pkg' best.png               # 退路：导出视觉主体贴图（原始分辨率）
```

通用选项：`--time=秒`（动画/视频取帧时间，默认 1.0）、`--debug`、`--verbose`、`--nozoom`、
`--text`（尝试渲染文字层，默认跳过）、`--ffmpeg=路径`、`--cache=目录`。

`render` 成功时 stdout 最后会输出一行机器可读统计：

```
STATS drew=2 skipped=4 video=2 out=1320x860 ms=2471 peakmb=468
```

---

## 2. 实测出的容器格式（逆向结论）

### 2.1 PKG 容器

```
int32 magicLen(=8) + "PKGV00xx"
int32 entryCount
entryCount × { int32 nameLen; byte name[nameLen] (UTF-8); int32 offset; int32 length }
== 表结束位置 = dataStart ==
条目数据 = 文件偏移 (dataStart + offset)，长度 length
```

- `offset` 从 0 起连续累加；已用 3470764447 验证 `dataStart=3076, fileLen=336160071, 最后 offset+length=336156995 == fileLen-dataStart`。
- 本机 48 个包版本分布：PKGV0002×1、0011×1、0018×2、0019×3、0020×2、0021×12、0022×9、0023×9、0024×9。
  所有版本都符合上面的布局（magic 后紧跟 int32 条目数）。
- 条目名是包内相对路径（`scene.json`、`materials/*.json/tex`、`models/*.json`、`shaders/...`、`fonts/*.ttf` 等）。

### 2.2 TEX 贴图（`TEXV0005` / `TEXI0001`）

**重要：字符串是「零终止 C 字符串」，不是 int32 长度前缀。**

```
"TEXV0005\0"
"TEXI0001\0"
int32 texFormat;          // 0=RGBA8888, 4=DXT5, 6=DXT3, 7=DXT1, 8=RG88, 9=R8
int32 flags;              // 1=NoInterpolation, 2=ClampUVs, 4=IsGif, 32=IsVideoTexture
int32 textureWidth, textureHeight;
int32 imageWidth, imageHeight;      // 真实图像尺寸（tex 可能带 padding，需 crop）
uint32 unk;
"NString 容器magic"        // TEXB0001 / TEXB0002 / TEXB0003 / TEXB0004
```

- `TEXB0003` 后再读 `int32 imageFormat`（FreeImage 格式；`-1`=UNKNOWN）；
  `TEXB0004` 后读 `int32 imageFormat` + `int32 isVideoMp4`。
- 注意：repkg 里 `FreeImageFormat` 的枚举值**不是**标准 FreeImage 头文件的值：
  `UNKNOWN=-1`、`BMP=0`、`JPEG=2`、`PNG=13`、`MP4=35` 等（实测 `赞助.tex` 的 imageFormat=13 且 mip0 就是完整 PNG 文件）。
- 每个 image 先读 `int32 mipmapCount`，再按容器版本读 mipmap：
  - V1（TEXB0001）：`w, h, byteCount, bytes`
  - V2 / V3：`w, h, isLZ4(==1), decompressedByteCount, byteCount, bytes`
  - V4（TEXB0004）：`int32 mustBe1, int32 mustBe2, NString 条件JSON, int32 mustBe1, w, h, isLZ4, decompressedByteCount, byteCount, bytes`
  - **坑**：实测某些 TEXB0004 的贴图（imageFormat 非 MP4）在 data 区实际用的是 V3 的 mipmap 布局；
    实现里用「TEXB0004 且 videoFlag==1 才按 V4 读，否则按 V3 读」并用 mip0 开头是否含 `ftyp` 兜底，48 个包全部通过。
- LZ4 块用 `K4os.Compression.LZ4.LZ4Codec.Decode(src,0,srcLen,dst,0,decompressedByteCount)` 解压。
- DXT1/3/5：标准 BC1/BC2/BC3 解码。实现坑：一个 4x4 block 解出 64 字节 RGBA（不是 16），
  否则后续 block 会错位。
- 视频贴图（探测顺序：mip0 前 12 字节含 `ftypmp42/ftypisom/ftypmsnv`，或 flags 含 `IsVideoTexture`，或 TEXB0004 videoFlag=1）：
  把 mip0 原始字节整份落盘成 `.mp4`，再用 ffmpeg 抽一帧（默认 `-ss 1.0`，避开开头淡入）。
- 內嵌图片贴图（imageFormat>0）：mip0 的字节本身就是一整份图片文件（实测 PNG），直接 `Image.Load` 解码。
- 实测例子：
  - `materials/窗边的伊蕾娜 昼夜变化.tex`：TEXB0003，RGBA8888，flags=ClampUVs|IsVideoTexture，3840x2160，mip0=102,479,469 字节 MP4。
  - `materials/伊蕾娜提示框 2.tex`：TEXB0003，DXT5，texture=2368x1280、image=2307x1228，5 级 mipmap，每级 LZ4。
  - `materials/赞助.tex`：TEXB0004，imageFormat=13(PNG)，1000x471，mip0 就是 PNG 文件。

### 2.3 scene.json 场景描述

顶层 `{ camera, general, objects, version }`：

- `general.orthogonalprojection`：设计画布 `width/height`（常见 3840x2160，也有 1366x768、6080x3420 等）。
- `general.zoom`：画布缩放（可能是数字，也可能被一个 `“zoom” 动画对象`覆盖，实测 2887099508 的 zoom 是从 3→1 的动画）。
- `general.clearcolor`：背景色（`"r g b"`，0~1 浮点）。
- `objects[]`：每项可能有 `id`、`parent`（父对象 id）、`origin:"x y z"`、`size:"w h"`、`scale:"x y z"`、
  `angles:"x y z"`（弧度）、`alpha`、`visible`、`image:"models/*.json"`、`text/font/pointsize`、`particle`、`sound`。
- **父子坐标是累加的**：子对象 `origin` 相对父对象 `origin`（实测父 2055.59619,1262.86682 + 子 -135.59619,-182.86652 = 1920,1080 画布中心）。
- 图片层解析链：`object.image` → `models/*.json`（2D 图像模型：`{"material":"materials/xxx.json","width":W,"height":H,"nopadding":true}` 或 `autosize`）
  → `materials/*.json`（`{"passes":[{"blending":"translucent","shader":"genericimage4","textures":["贴图名"]}]}`）
  → `materials/<贴图名>.tex`。
- **可见性规则**（本工具采用）：`visible` 是 bool 就直接用；是 `{user:{condition,name}, value:bool}` 就用 `value`
  （这是编辑器保存时的默认状态，通常等于作者预期）；带 `script` 的按 `value`（没有 value 则视为可见）。
  例如 3470764447 的背景容器 visible 带 script，但 scene.json 里 `value=false` 的对象（清晨/黄昏/夜晚）确实被跳过，只留白天层。
- **alpha 动画**：`alpha` 可能是 `{ animation:{ c0:[{frame,value}...], options:{fps,length,mode}}, value:1 }`，按 `--time` 求值。
  实测 3025975631 / 3470764447 的「提示框/署名层」在 t=1s 时 alpha=0（作者设计成几秒后淡入），所以默认输出里看不到提示框——当背景反而更好。
- 文字层（`text/font/pointsize`）默认跳过（本工具不描字）；粒子层（`particle`）跳过；`sound` 忽略。
- 内置模型（`models/util/composelayer.json`、`solidlayer.json`、`fullscreenlayer.json`、`projectlayer.json` 等）
  **不在 pkg 里**，是 Wallpaper Engine 引擎内置的合成层，本工具按“缺失模型”跳过并计入 skip 统计。

### 2.4 本工具的合成语义

1. 以 `general.orthogonalprojection` 为设计画布，`cover` 语义映射到目标宽高：
   `s = max(outW/baseW, outH/baseH) * zoom`，画布中心对准输出中心（左右或上下裁切）。
2. 每层世界变换：`T(父原点累加) · R(z 角度) · S(层 scale × 模型尺寸)`；层的 quad 宽度 = 模型 width（或 size 字段）。
3. 逆仿射映射 + 双线性采样，`source-over` 混合；`blending:additive` 用加法；`masked/遮罩` 暂不支持。
4. 背景先填 `clearcolor`。
5. 动画/视频统一取 `--time`（默认 1.0s）时刻。

---

## 3. 支持的壁纸比例 / 尺寸

- 本机场景的原生画布绝大多数是 16:9（3840x2160），也有 1366x768（3653963863）、6080x3420（2887099508）。
- `render` 接受任意输出宽高，按 cover 裁切：1320x860（比例 1.535，适合目标窗口，会裁掉 16:9 左右各约 6.8%）、2560x1440（16:9，不裁）都可以。
- 1024x768 / 1080x1920 等非 16:9 输出同样可用（会按 cover 裁切）。

---

## 4. 48 个场景实测结果

- 全部 48 个包：`list`/`info`/`render` 均成功，48/48 输出 PNG，无异常退出。
- 全量 660x430 试渲总耗时 49.6s（平均 1.03s/个，含视频抽帧；缓存已热）。24 个场景只有 1 个可见图像层。
- `skip`（缺失内置模型/隐藏/文字/粒子等）≥5 的场景：2887099508(drew=6,skip=14)、3266921288(33,5)、3395860220(2,5)、3461168300(5,6)。
- 视觉抽查约 40 个正常/接近正常；下列 8 个有明显缺陷（按原因分类，详见 §5 的失败分类）：
  - 2386275267：近空白（有效层全被脚本/条件隐藏或只有全屏 shader 层）
  - 2887099508：三维模型场景（5 个 `.mdl` + 透视相机），2D 层叠无法还原 → 输出是碎裂的人体部件特写
  - 3266921288：发白（大量内置合成层缺失，只剩透明/白底）
  - 3400879974：很暗近似空白（内置 layer 缺失 + 深色场景）
  - 3404976219、3484560439、3785079210：画面中出现灰色矩形空洞（依赖内置遮罩/合成层或 shader 效果的贴图/区域）
  - 3597116797：极端局部特写（镜头裁切/3D 视角，主体在画面外）
  - 3320489297：拼贴/模糊（多层叠加 + 缺失后处理）

统计原始数据：`_out\survey\survey.csv`、`_out\survey\survey.log`、联系表 `_out\survey\montage.png`。

---

## 5. 已知限制（含失败原因分类）

| 类别 | 原因 | 表现 | 例子 |
|---|---|---|---|
| 格式不支持 | 三维 `.mdl` 模型/透视相机（非 2D 层叠） | 画面碎裂、只剩局部贴图 | 2887099508 |
| 格式不支持 | 引擎内置合成层（composelayer/solidlayer/fullscreenlayer/projectlayer/遮罩层）不在 pkg 内 | 纯色遮罩、边框、后处理丢失 → 灰块/发白/近空白 | 3266921288、3400879974、3404976219、3484560439、3785079210 |
| 格式不支持 | 全屏 shader 后处理（bloom/模糊/扭曲/溶解），本工具不做 | 无辉光/无模糊/颜色偏硬 | 所有带 effects 的场景 |
| 坐标算不准 | 相机透视/eye、parallaxDepth≠0 视差、3D 布局 | 局部裁切/主体出画 | 3597116797、2887099508 |
| 粒子为主 | 粒子层跳过（可能占画面主体） | 只剩底图 | 3653963863（水花粒子）等 |
| 文字缺失 | 文字层不描字 | 时钟/署名/说明文字缺失 | 3025975631 的「12:34」、3470764447 的署名 |
| 动画取帧 | 视频贴图仅取 `--time` 单帧 | 与动态壁纸有差异（水流/云/昼夜进度） | 3025975631、3470764447、2992022633 |
| 混合近似 | 只支持 translucent/additive 两种混合 + 无 effects 栈 | 部分叠层颜色/亮度与 WE 不同 | — |

补充：
- 视频贴图的抽帧会把 TEX 里整段 MP4 先写到缓存目录再用 ffmpeg 抽帧；300MB 级的包会临时占用几百 MB 磁盘（见 §6）。
- 提示框/署名层由于 alpha 动画在 t=1s 为 0，默认不可见；如需还原可改 `--time` 到动画中段（例如 12s）。
- TEX 支持 RGBA8888/DXT1/DXT3/DXT5/R8/RG88 与內嵌 PNG/JPEG/其它 ImageSharp 可解图片；GIF 贴图只取第一帧。

---

## 6. 与 WPF 接入的建议

- **推荐做成“启动时异步生成一次，缓存到 `data\images\scene\<id>.jpg`”**：
  单张渲染实测 0.8–3.1s、峰值内存 54–482MB（视频贴图越多越大），对启动是“可接受的慢”，务必放后台线程/独立进程，
  先显示占位色或 1079 封面，渲染完再替换。JPEG quality=92 时 1320x860 仅约 169KB（实测 3025975631）。
- **首选独立进程方式**（进程退出即释放内存，不污染 WPF 进程）：
  ```powershell
  SceneRender.exe render <scene.pkg> 1320 860 <data>\images\scene\<id>.jpg --cache <data>\cache\scene
  ```
  WPF 侧 `Process.Start` + `WaitForExitAsync`，失败/超时回退封面图（`preview.gif` 或 1079 封面）。
- 磁盘：视频贴图抽帧会在 `--cache` 目录写 mp4 + png（单包最大约 300–500MB，缓存按「贴图名 + 字节数」的稳定 FNV-1a 哈希 + 抽帧时间戳命名；
  同一张图二次渲染会命中缓存，实测 2992022633 从 1.1s 降到 0.5s）。本工具不会自动清理缓存；建议渲染完成后删除该包对应的缓存文件，
  或整个 `cache\scene` 定期清空。实测本机 48 个包全量渲染后缓存约 2.5GB —— 务必配置清理策略。
- `ffmpeg` 不在 PATH 时要 `--ffmpeg=<path>`；本机实际路径 `F:\ffmpeg\ffmpeg-8.0.1-essentials_build\bin\ffmpeg.exe`（工具已内置该路径探测）。
- 若想改成类库内嵌：代码不依赖 WPF，只有 ImageSharp + K4os 两个包；把 `Program.cs` 的 `CmdRender` 抽成 API 即可。
  注意 64 位进程 + 大视频场景峰值 ~500MB，和 WPF 自身内存叠加。
- 输出尺寸：目标窗口 1320x860 就用 1320x860；要 16:9 全画幅用 2560x1440。cover 裁切会切掉 16:9 素材的左右边缘，
  若窗口更宽可改 2560x1080 之类。

---

## 7. 工程结构与复现

```
tools\SceneRender\
  SceneRender.csproj     # net10.0-windows, x64, ImageSharp + K4os.Compression.LZ4
  Program.cs             # CLI：list / tex / info / render / main / dump
  Pkg.cs                 # PKG 容器读取（场景包抽象）
  Tex.cs                 # TEX 解析 + LZ4/DXT/內嵌图片/视频 MP4 识别
  Dxt.cs                 # DXT1/3/5 解码（BC1/BC2/BC3）
  Scene.cs               # scene.json 解析（System.Text.Json；对重复键 last-wins）
  Renderer.cs            # 层解析、cover 映射、逆仿射双线性合成、视频抽帧缓存
  survey.ps1             # 48 个场景批量试渲 + 统计
  montage.ps1            # 把试渲结果拼联系表
  _out\final\            # 交付 PNG（见 §0 表）
  _out\survey\           # 48 张试渲 + montage.png + survey.csv/log
  _ref\                  # 参考：repkg 解析源码（只读参考，未参与编译）
```

复现：

```powershell
cd F:\订单管理\tools\SceneRender
dotnet build -c Release
dotnet bin\Release\net10.0-windows\SceneRender.dll render `
  'F:\steam\steamapps\workshop\content\431960\3025975631\scene.pkg' 1320 860 _out\final\3025975631_1320x860.png
```

注意：`Scene.cs` 用 `System.Text.Json` 是因为部分 `scene.json` 里存在重复键（如 `Phase`/`phase`），
PowerShell 5.1 的 `ConvertFrom-Json` 会直接失败。
