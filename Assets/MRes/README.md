# MRes Runtime Loader

按 `Assets/Docs/MRes_Runtime_Loader_Workflow_xLua.md` 实现的独立运行时加载器。Unity 2022.3，使用项目 `Assets/XLua` 的真实 xLua 和 `Assets/Plugins` 的 native plugin。`MRes.Runtime` 只引用 `XLua.Runtime`，不依赖游戏程序集、UnityEditor 或 Packer。

## 直接体验

打开 `Assets/MRes/Samples/MResRuntimeDemo.unity`，进入 Play Mode。场景从同一份 `BouncePad.mres.bytes` 加载两个弹射台；小球轮流落到两个台上，由包内 Lua 的 `OnCollisionEnter` 施力弹起。两个实例共享一个 LuaEnv，各自持有一个 scriptEnv。

示例包是原始 `.mres` 二进制加 `.bytes` 后缀，供 Unity 当成不透明 TextAsset 引用；没有 ScriptedImporter，也不会把包内条目解包到磁盘。`Tests/Fixtures` 只构造固定验收数据，不包含源 Prefab 导出器或生产 Packer。

`DemoShaderRegistry.asset` 明确将 `MRes/Unlit` 绑定到 `MRes/DemoUnlit`。实际项目请创建自己的 Shader Registry 资产，填入对应 Shader；缺失、重复或当前平台不支持的 shader ID 会抛出 `MResShaderException`，没有 fallback。

## API

```csharp
using MRes;

// 在 Unity 主线程配置注册表；也可向每次 Load 直接传 registry。
MResLoader.ShaderRegistry = registry;
MResLoadHandle a = MResLoader.Load(absolutePath);
MResLoadHandle b = MResLoader.Load(bytes);
MResLoadHandle c = MResLoader.Load(stream);

// Read 只读取、校验和解析，不创建 UnityEngine.Object，也不初始化 LuaEnv。
MResPackage package = MResLoader.Read(bytes);
MResLoadHandle d = MResLoader.Load(package, registry);
d.Root.transform.position = new UnityEngine.Vector3(0, 1, 0);
UnityEngine.Debug.Log(d.Metadata.name);

// 对每个 handle 调用一次；重复调用也安全。
a.Dispose(); b.Dispose(); c.Dispose(); d.Dispose();
```

`Read` 和 `Load` 均支持 path / byte[] / Stream。Stream 从当前 Position 开始读到结尾，支持不可 Seek 的流，不关闭调用方的流。byte[] 的内容被复制；外部改写输入数组不会影响已解析包。`MResPackage.Metadata` 返回副本，每次 Load 有独立 Metadata。

`Load` / `Dispose` / Lua API 必须在 Unity 主线程调用。Play Mode 使用 `Destroy`，资源会在帧末销毁；Edit Mode 使用 `DestroyImmediate`。加载失败会回滚当次创建的全部节点和资源。直接销毁已激活的 Root 时，内部生命周期组件也会释放资源；从未激活的 Root 在 Unity 中不保证收到 OnDestroy，因此仍应以 `handle.Dispose()` 为主要使用方式。

示例 `MResSpawner` 可直接挂到场景对象，指定 `path` 或 `packageBytes`，并指定 registry；其 OnDestroy 自动 Dispose。

`Load(package, registry, options)` 可传入 `MResLoadOptions`。`ExecuteLua=false` 用于完全不执行脚本的预览，`ActivateRoot=false` 便于先设定父节点、物理场景和位置再启用。`SharedScript` 可绑定调用方持有的 `MResLuaScript`；销毁视图只解除绑定，脚本由调用方 Dispose。`MResLuaScript.Invoke` 支持项目自定义事件，`HasFunction` 检查事件是否存在。默认 Load 仍独占脚本并随 handle 释放。

本项目游戏装置直接使用原始 `.mres`，目录与 Lua 游戏 API 见 [Resources/Devices](../Resources/Devices/README.md)。编辑器将原包字节收录进 Resources 目录资产，Player 不依赖磁盘 Assets 路径。

## 二进制格式

所有数值为 **little-endian**，路径和 JSON/Lua 文本为严格 UTF-8（文本允许 UTF-8 BOM）。没有 SHA、CRC 或其他 MRes hash 字段；PNG 内部的标准结构保留，Loader 不自行计算 CRC。

Header 固定 28 bytes：`MRES[4], UInt16 version=1, UInt16 flags=0, UInt32 entryCount, UInt64 tocOffset, UInt64 tocSize`。

每个 TOC 条目：`UInt32 pathByteLength, UTF8 path, Byte type, Byte compression=0, UInt16 reserved=0, UInt64 offset, UInt64 size`。偏移以输入包起点为零。

类型：Manifest=1 / Prefab=2 / Mesh=3 / Material=4 / Texture=5 / Lua=6 / Metadata=7 / PhysicsMaterial=8 / Other=255。必须恰好有一个 Manifest 类型条目，文件名可自定。资源不能覆盖 Header、TOC 或其他条目；TOC 大小和条目数必须完全吻合。

虚拟路径大小写敏感，使用 `/`。拒绝绝对路径、空段、`.` / `..`、反斜线、控制字符、冒号及 Windows 非法文件名字符、段首尾空白、尾随点。资源引用必须存在、类型正确，且必须列在 Manifest 的相应数组中。

输入限制：包 256 MiB、65536 条目、路径 1024 UTF-8 bytes、JSON 深度 64、10000 节点、单 Mesh 100 万顶点 / 600 万索引 / 1024 submeshes；单 PNG 8192×8192 以内且最多 16777216 像素。错误以 `MResFormatException` / `MResMissingResourceException` 报告。

## JSON Schema 补全约定

原需求未指定 `.meshbin` 字节布局、组件 JSON 形状和纹理设置存储位置。以下是本实现补全的明确契约；外部制作工具应对齐这些约定，不能假设任意同名 `.mres` 都兼容。源需求文档未被改写。

### Manifest / Metadata

```json
{
  "format": "mres", "version": 1, "packageName": "BouncePad",
  "prefab": "prefab/root.json", "metadata": "metadata/metadata.json",
  "meshes": ["meshes/0.meshbin"],
  "materials": ["materials/0.json"],
  "textures": ["textures/0.png"],
  "physicsMaterials": ["physics_materials/0.json"],
  "lua": "scripts/main.lua",
  "textureSettings": {
    "textures/0.png": {
      "name": "PadTexture", "mipChain": true, "linear": false,
      "wrapModeU": 0, "wrapModeV": 0, "filterMode": 1, "anisoLevel": 1
    }
  }
}
```

四种资源数组可以省略（视为空数组）。`lua` 可省略或设为 null，此时完全不创建 Lua Behaviour。`textureSettings` 是按纹理路径索引的可选对象，上述值也是缺省设置；未指定 name 时使用虚拟路径。WrapMode / FilterMode 使用 Unity 枚举整数。

```json
{"name":"弹射装置","description":"碰撞后将目标向上弹射。","value":120,"type":"device"}
```

Metadata 四个字段都必填；value 必须是有限 float，type 只接受大小写完全匹配的 `medicine` / `device`。JSON 重复字段、非法数值及多余尾随内容均拒绝。

### Mesh：MMSH v1

| 顺序 | 数据 |
|---|---|
| 1 | 4 bytes `MMSH`, UInt16 version=1 |
| 2 | Byte indexFormat：0=UInt16 / 1=UInt32；Byte attributeFlags |
| 3 | UInt32 vertexCount、UInt32 submeshCount |
| 4 | 3×Float32 bounds.center；3×Float32 bounds.extents |
| 5 | vertexCount × 3×Float32 vertices |
| 6 | flags&1：vertexCount × 3×Float32 normals |
| 7 | flags&2：vertexCount × 4×Float32 tangents |
| 8 | flags&4：vertexCount × 2×Float32 uv0 |
| 9 | flags&8：vertexCount × 2×Float32 uv1 |
| 10 | flags&16：vertexCount × 4×Float32 RGBA colors |
| 11 | 每个 submesh：Int32 Unity MeshTopology、UInt32 indexCount、对应宽度的索引数组 |

属性按上述顺序分块存储，不交错。拓扑支持 Triangles=0、Quads=2、Lines=3、LineStrip=4、Points=5；索引必须在顶点范围内，数量必须满足拓扑规则。所有 float 必须有限；bounds.extents 非负。不重算法线、UV 或 bounds；未带 normals 的 Mesh 保持空 normals。

### Material / PhysicsMaterial

```json
{
  "name":"Pad", "shaderId":"MRes/Unlit", "renderQueue":-1,
  "enableInstancing":false, "doubleSidedGI":false, "keywords":[],
  "properties":[
    {"name":"_Color","type":"Color","value":{"r":1,"g":1,"b":1,"a":1}},
    {"name":"_MainTex","type":"Texture","value":"textures/0.png",
     "scale":{"x":1,"y":1},"offset":{"x":0,"y":0}}
  ]
}
```

属性类型大小写固定：Float / Range（float value）、Int（int value）、Color（r/g/b/a）、Vector（x/y/z/w）、Texture（路径或 null）。Texture scale/offset 可省略，默认 (1,1)/(0,0)。使用 `Material.SetInteger` 恢复真正的 Integer 属性。不存在的 Shader property 会使 Load 失败。不同 shader 的属性名/类型必须由 registry 的制作方和包制作方共同对齐。

```json
{"name":"Bounce","dynamicFriction":0.2,"staticFriction":0.3,"bounciness":0.5,
 "frictionCombine":2,"bounceCombine":3}
```

Combine 使用 Unity 枚举整数：Average=0 / Multiply=1 / Minimum=2 / Maximum=3。物理材质以虚拟路径缓存；同一路径的 Collider 共享同一个实例。

### Prefab

```json
{
  "nodes":[{
    "id":0,"parentId":-1,"name":"Pad","active":true,"layer":0,
    "transform":{
      "localPosition":{"x":0,"y":0,"z":0},
      "localRotation":{"x":0,"y":0,"z":0,"w":1},
      "localScale":{"x":1,"y":1,"z":1}
    },
    "components":[
      {"type":"MeshFilter","mesh":"meshes/0.meshbin"},
      {"type":"MeshRenderer","enabled":true,"materials":["materials/0.json",null]},
      {"type":"BoxCollider","center":{"x":0,"y":0,"z":0},"size":{"x":1,"y":1,"z":1}}
    ]
  }]
}
```

`id` 为非负整数且唯一，唯一 Root 的 parentId=-1。其余 parentId 必须引用本包节点，允许乱序，禁止环。transform 三个字段必填，rotation 是单位四元数。active 默认 true，layer 默认 0。所有节点先 inactive，挂载并初始化完 Lua 后恢复 active 状态。

组件采用扁平对象（type 与参数同级），仅接受以下七种类型；任何 Lua Behaviour 或未知组件都会拒绝：

| type | 字段 |
|---|---|
| MeshFilter | mesh（路径/null） |
| MeshRenderer | enabled（默认 true）、materials（保留顺序和 null 槽） |
| BoxCollider | center（默认零）、size（必填且正值） |
| SphereCollider | center、radius（默认 0.5） |
| CapsuleCollider | center、radius（0.5）、height（2，至少直径）、direction（1，即 Y） |
| MeshCollider | mesh（必填，triangle submeshes）、convex（false） |
| Rigidbody | mass（1）、drag（0）、angularDrag（0.05）、useGravity（true）、isKinematic（false）、interpolation（0）、collisionDetectionMode（0）、constraints（0） |

所有 Collider 的公共字段：enabled=true、isTrigger=false、contactOffset=0.01、physicsMaterial=null、includeLayers=0、excludeLayers=0、layerOverridePriority=0、providesContacts=false、hasModifiableContacts=false。include/exclude 为 Int32 位掩码。Collider 不恢复 bounds、attachedRigidbody 或 attachedArticulationBody，这些由 Unity 建立。

Unity 的 contactOffset 等设置依赖实际 PhysX shape。内部激活组件保留这些值，在节点激活后应用，并在后续启用 Collider 时于物理步前恢复；不会在构建时提前激活包。未激活 Collider 的 Unity getter 可能显示引擎缺省值，实际物理 shape 建立后才应用包中配置。

MeshFilter / MeshRenderer / Rigidbody 每个节点最多一个，可以有多个 Collider。非凸 MeshCollider 不能作为 Trigger，也不能附着到动态 Rigidbody。isKinematic 仅允许 Discrete 或 ContinuousSpeculative 检测模式。Unity 2022 使用 PhysicMaterial 和 drag；Unity 6 编译分支使用 PhysicsMaterial 和 linearDamping，包字段保持不变。旧版不存在的层覆盖/Contact API 由版本宏处理，最低实际验证版本为 Unity 2022.3。

## Lua 生命周期

```lua
function Start() print(self:GetName()) end
function Update(dt) end
function FixedUpdate(dt) end
function OnCollisionEnter(other) other:AddForce(0, 600, 0) end
function OnCollisionStay(other) end
function OnCollisionExit(other) end
function OnTriggerEnter(other) end
function OnTriggerStay(other) end
function OnTriggerExit(other) end
function OnDestroy() end
```

Loader 自动向 Root 添加内部 `MResRuntimeBehaviour`。静态子 Collider / 子 Rigidbody 的事件由内部 relay 转发到同一 Package Behaviour；不在 Schema 中序列化这些组件。多接触对按 Unity 事件数量转发。

脚本中有 `self` 和 `metadata`。Wrapper 支持 GetPosition、SetPosition、Translate（世界位移）、Rotate（局部欧拉角）、AddForce（ForceMode.Force）、SetVelocity，以及 GetName / GetDescription / GetValue / GetItemType（返回 Medicine / Device）。碰撞对象的 Rigidbody 会沿父级查找；没有 Rigidbody 时施力/设速度无操作。

每个实例的普通全局变量和 `_G` 都指向各自的 scriptEnv；标准库通过共享 LuaEnv 继承。它不是安全沙箱：共享标准库、CS 和 require 仍属于 xLua，不能用来执行不可信脚本；本版本只执行 manifest.lua，不提供包内多文件模块解析。

一个自动创建的持久 Runtime Driver 每帧 Tick。实例释放调用 Lua OnDestroy 并释放所有 LuaFunction / LuaTable，不关闭全局 VM。Driver/应用结束或 `MResLuaRuntime.Dispose()` 会先释放全部已注册 Behaviour，再销毁 LuaEnv。静态状态在 SubsystemRegistration 重置，兼容关闭 Domain Reload 的 Play Mode 重入。

初始化错误抛 `MResLuaException` 并回滚加载；运行中的事件异常记录带包名/事件名的异常，并停用该实例的后续回调。OnDestroy 即使出错也会继续释放引用。

## 验证与平台

Test Runner 中分别运行 `MRes.Tests` 的 EditMode 和 PlayMode 测试。覆盖读取/重建、材质参数、共享物理材质、非法格式/引用/层级、异常回滚、真正的碰撞施力、子节点 Trigger、独立实例状态、共享 LuaEnv、销毁与资源回收。

2026-10-10 验证结果：Windows / Unity 2022.3.62f3c1，EditMode **22/22**、PlayMode **6/6** 通过。游戏接入通过 15 包资源检查、664 项玩法断言、11 项放置检查和 63 项真实场景跨回合检查。Windows Standalone（Mono / D3D11）已构建并实际运行，验证 Resources 目录、15 个包的网格与 xLua、Lua 银行逻辑。报告和验证版位于项目 `.utmp`；临时转换/验证脚本已移出 Assets。Runtime Loader 不依赖 UnityEditor / AssetDatabase / ScriptedImporter / PrefabUtility，也不向磁盘解包。

`MRes → Recreate Acceptance Demo` 可重建固定示例资源和测试场景。编辑器辅助工具和测试均在独立测试程序集，完全不进入 Runtime Loader。

依赖保留项目现有 xLua 内容，添加程序集边界，并通过 `XLua/Generate Code` 生成 `XLua/Gen` 中的绑定代码，消除构建后的缺失绑定检查异常。程序集命名 `XLua.Runtime`，避免与 native `xlua.dll` 同名；Editor 文件通过独立 asmdef/asmref 分离。许可证见 `XLua-LICENSE.txt`；官方说明：[Tencent/xLua](https://github.com/Tencent/xLua)。

已验收 Windows Editor 和 Windows Standalone Mono。IL2CPP 仍需对应平台 xLua native plugin，并在目标平台验证 AOT/裁剪设置。Wrapper/Metadata/Vector3 和游戏 Lua 接口已通过 link.xml 保留。
