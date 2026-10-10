# MRes Runtime Loader Workflow — Unity 动态解析与 Prefab 重建需求（xLua 版）

## 1. 项目目标

实现一个**独立 Unity Runtime Loader**，动态读取 `.mres` 并在内存中重建物体。

不要求 `.mres` 在 Project 窗口中展开，不使用 `ScriptedImporter`。

目标流程：

```text
path / byte[] / Stream
        ↓
MResLoader
        ↓
读取 .mres
        ↓
校验
        ↓
解析 Prefab Schema
        ↓
动态创建 Texture2D
        ↓
动态创建 Mesh
        ↓
动态创建 Material
        ↓
创建 GameObject Hierarchy
        ↓
恢复 Collider / Rigidbody
        ↓
挂 MResRuntimeBehaviour
        ↓
使用 xLua 执行脚本
        ↓
返回 Root GameObject
```

`.mres` 内容不得解包到 `Assets/` 或任何临时磁盘目录。

---

# 2. 核心约束

Loader 必须是 Runtime Assembly。

禁止：

```text
AssetDatabase
UnityEditor
ScriptedImporter
PrefabUtility
Editor-only API
```

必须能运行于：

```text
Editor Play Mode
```

并为未来 Standalone Player 保留兼容性。

---

# 3. xLua 为固定 Lua Runtime

本项目固定使用：

```text
xLua
```

不需要设计可替换 Lua VM 抽象层。

源 Prefab 和 MRes Prefab Schema **完全不知道 Lua Behaviour 的存在**。

如果 `manifest.lua` 存在，Loader 在 Root GameObject 重建完成后自动添加一个内部桥接组件：

```csharp
MResRuntimeBehaviour
```

该组件属于 MRes Runtime 内部实现，不要求资源制作者手动挂载。

架构：

```text
MRes Runtime
├─ MResLoader
├─ MResLuaRuntime
└─ xLua
```

必须遵守：

> 整个运行环境尽量共享一个 `LuaEnv`，不要每个 MRes 实例创建一个 `LuaEnv`。

每个 MRes Lua Behaviour 使用独立：

```text
LuaTable scriptEnv
```

从而做到：

```text
一个 LuaEnv
    ↓
多个 MRes 物体
    ↓
每个物体独立 LuaTable
```

---

# 4. Metadata Schema

Metadata 固定：

```json
{
  "name": "弹射装置",
  "description": "碰撞后将目标向上弹射。",
  "value": 120,
  "type": "device"
}
```

字段：

| 字段 | C# 类型 | 含义 |
|---|---|---|
| `name` | string | 名字 |
| `description` | string | 描述 |
| `value` | float | 价值 |
| `type` | enum/string | `medicine` 或 `device` |

推荐：

```csharp
public enum MResItemType
{
    Medicine,
    Device
}

[Serializable]
public sealed class MResMetadata
{
    public string name;
    public string description;
    public float value;
    public MResItemType type;
}
```

读取 JSON 时：

```text
"medicine" -> Medicine
"device"   -> Device
```

其他值视为非法格式。

---

# 5. 与 Packer 完全解耦

Loader 只依赖：

```text
MRes v1 Binary Format
MRes Prefab Schema
MRes Material Schema
MRes Metadata Schema
MRes Shader ID Contract
MRes xLua Behaviour Contract
```

禁止引用 Packer Assembly。

---

# 6. API

至少提供：

```csharp
public static class MResLoader
{
    public static MResLoadHandle Load(string filePath);

    public static MResLoadHandle Load(byte[] bytes);

    public static MResLoadHandle Load(Stream stream);
}
```

并提供：

```csharp
public static MResPackage Read(string filePath);
public static MResPackage Read(byte[] bytes);
public static MResPackage Read(Stream stream);
```

区别：

```text
Read()
    只解析，不创建 Unity Object

Load()
    Read + 动态实例化
```

`MResLoadHandle` 至少包含：

```csharp
public GameObject Root { get; }
public MResMetadata Metadata { get; }
```

---

# 7. MResLuaRuntime

实现全局 xLua Runtime：

```csharp
using XLua;

public static class MResLuaRuntime
{
    private static LuaEnv s_LuaEnv;

    public static LuaEnv Env
    {
        get
        {
            if (s_LuaEnv == null)
                s_LuaEnv = new LuaEnv();

            return s_LuaEnv;
        }
    }

    public static void Tick()
    {
        s_LuaEnv?.Tick();
    }

    public static void Dispose()
    {
        s_LuaEnv?.Dispose();
        s_LuaEnv = null;
    }
}
```

需要提供一个 Runtime Driver：

```csharp
public sealed class MResLuaRuntimeDriver : MonoBehaviour
{
    private void Update()
    {
        MResLuaRuntime.Tick();
    }

    private void OnDestroy()
    {
        MResLuaRuntime.Dispose();
    }
}
```

项目中只需要一个 Driver。

不要每个 MRes 对象创建 LuaEnv。

---

# 8. `.mres v1` Header

MRes v1 **不使用 SHA、CRC 或其他 Hash 字段**。Loader 只做文件结构、范围、路径和内部引用校验。

固定 28 bytes：

| 字段 | 类型 |
|---|---|
| Magic | 4 bytes `MRES` |
| Version | UInt16 |
| Flags | UInt16 |
| EntryCount | UInt32 |
| TocOffset | UInt64 |
| TocSize | UInt64 |

要求：

```text
Version == 1
Flags == 0
```

---

# 9. TOC Entry

| 字段 | 类型 |
|---|---|
| PathLength | UInt32 |
| Path | UTF-8 byte[] |
| Type | Byte |
| Compression | Byte |
| Reserved | UInt16 |
| Offset | UInt64 |
| Size | UInt64 |

Entry Type：

```csharp
public enum MResEntryType : byte
{
    Manifest = 1,
    Prefab = 2,
    Mesh = 3,
    Material = 4,
    Texture = 5,
    Lua = 6,
    Metadata = 7,
    PhysicsMaterial = 8,
    Other = 255
}
```

---

# 10. 校验

创建任何 Unity Object 前必须完成：

```text
Magic
Version
Flags
TOC Range
Entry Count
Entry Offset
Entry Size
Duplicate Path
非法路径
Compression
Manifest
Prefab Entry
Metadata Schema
内部引用
```

异常类型建议：

```csharp
MResFormatException
MResMissingResourceException
MResShaderException
MResInstantiationException
MResLuaException
```

---

# 11. Manifest

示例：

```json
{
  "format": "mres",
  "version": 1,
  "packageName": "BouncePad",

  "prefab": "prefab/root.json",

  "meshes": [
    "meshes/0.meshbin"
  ],

  "materials": [
    "materials/0.json"
  ],

  "textures": [
    "textures/0.png"
  ],

  "physicsMaterials": [
    "physics_materials/0.json"
  ],

  "lua": "scripts/main.lua",
  "metadata": "metadata/metadata.json"
}
```

必须验证：

```text
format == "mres"
version == 1
```

---

# 12. 加载顺序

严格：

```text
1. Read Header
2. Read TOC
3. Validate
4. Read Manifest
5. Parse Metadata
6. Read Lua Source
7. Create Texture2D
8. Create Mesh
9. Create PhysicsMaterial
10. Resolve Shader
11. Create Material
12. Parse Prefab Schema
13. Create GameObject Nodes
14. Restore Hierarchy
15. Add Render Components
16. Add Physics Components
17. If manifest.lua exists, automatically add MResRuntimeBehaviour to Root
18. Initialize xLua Script
19. Activate Root
20. Return MResLoadHandle
```

---

# 13. Texture 动态创建

从 `.mres` 读取 PNG bytes：

```csharp
Texture2D texture = new Texture2D(
    2,
    2,
    TextureFormat.RGBA32,
    mipChain,
    linear
);

texture.LoadImage(bytes);
```

恢复：

```text
name
wrapModeU
wrapModeV
filterMode
anisoLevel
```

缓存：

```text
virtualPath -> Texture2D
```

---

# 14. Mesh 动态创建

恢复：

```text
indexFormat
vertices
normals
tangents
uv0
uv1
colors
submesh topology
indices
bounds
```

建议：

```csharp
mesh.SetVertices(...)
mesh.SetNormals(...)
mesh.SetTangents(...)
mesh.SetUVs(...)
mesh.SetColors(...)
mesh.SetIndices(...)
```

已有 Normal 时禁止无条件 `RecalculateNormals()`。

---

# 15. Shader Registry

`.mres` 不带 Shader 源码。

目标项目必须提供 Shader Registry。

例如：

```csharp
public sealed class MResShaderRegistry : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string id;
        public Shader shader;
    }

    public Entry[] entries;

    public Shader Resolve(string shaderId)
    {
        ...
    }
}
```

示例：

```text
MRes/PBR
MRes/Unlit
MRes/Transparent
MRes/Emission
MRes/Hologram
```

找不到 shaderId 时：

```text
Load 失败。
```

禁止静默 fallback。

---

# 16. Material 动态创建

读取：

```text
shaderId
renderQueue
enableInstancing
doubleSidedGI
keywords
properties
```

创建：

```csharp
Shader shader = shaderRegistry.Resolve(data.shaderId);
Material material = new Material(shader);
```

恢复：

```text
Float
Range
Int
Color
Vector
Texture
Texture Scale
Texture Offset
```

Texture 使用虚拟路径查 Texture Cache。

---

# 17. Prefab Schema

Prefab 是 MRes 自定义描述。

Prefab Schema 只描述物体结构和受支持 Unity 组件，不包含任何 xLua/MRes 行为组件。Lua 只通过 Manifest 与整个 Package 关联。

节点包含：

```text
id
parentId
name
active
transform
components
```

---

# 18. GameObject 重建

两阶段：

## Phase A

创建全部节点：

```csharp
Dictionary<int, GameObject> nodeMap;
```

## Phase B

恢复：

```text
Parent
localPosition
localRotation
localScale
Components
```

建议构建过程中先保持 inactive，全部完成后再恢复 Active 状态。

---

# 19. MeshFilter / MeshRenderer

MeshFilter：

```csharp
filter.sharedMesh = meshCache[path];
```

MeshRenderer：

```csharp
renderer.sharedMaterials = ...
```

必须保持 Material Slot 顺序。

---

# 20. Collider

Loader 必须恢复所有 MRes v1 规定的、可配置且会影响物理行为的 Collider 属性。

不恢复以下运行时派生属性：

```text
bounds
attachedRigidbody
attachedArticulationBody
```

这些由 Unity 自动计算或建立。

## 20.1 公共 Collider 属性

所有 Collider 均恢复：

```text
enabled
isTrigger
contactOffset
physicsMaterial
includeLayers
excludeLayers
layerOverridePriority
providesContacts
hasModifiableContacts
```

如果：

```text
physicsMaterial == null
```

则不绑定 Physics Material。

不同 Unity 版本中若部分 Layer Override / Contact API 不存在，使用版本宏兼容，但不得改变 MRes Schema。

## 20.2 BoxCollider

恢复：

```text
center
size
```

## 20.3 SphereCollider

恢复：

```text
center
radius
```

## 20.4 CapsuleCollider

恢复：

```text
center
radius
height
direction
```

## 20.5 MeshCollider

恢复：

```text
mesh
convex
```

并：

```csharp
collider.sharedMesh = meshCache[path];
```

## 20.6 PhysicsMaterial

`.mres` 中的：

```text
physics_materials/*.json
```

必须动态重建为 Unity `PhysicsMaterial`。

恢复：

```text
name
dynamicFriction
staticFriction
bounciness
frictionCombine
bounceCombine
```

缓存：

```text
virtualPath -> PhysicsMaterial
```

同一 Physics Material 被多个 Collider 引用时必须共享同一个动态实例。

---

# 21. Rigidbody

恢复：

```text
mass
drag
angularDrag
useGravity
isKinematic
interpolation
collisionDetectionMode
constraints
```

如 Unity 新版本使用 `linearDamping / angularDamping`，可通过版本宏适配。

MRes Schema 名称保持稳定。

---

# 22. Package 级 Lua 与 MResRuntimeBehaviour

Lua 属于整个 `.mres` Package，而不是 Prefab Node。

Prefab Schema 中禁止出现：

```text
MResLuaBehaviour
MResRuntimeBehaviour
Lua Component
```

Loader 读取：

```json
{
  "lua": "scripts/main.lua"
}
```

后，在 Root GameObject 完整重建完成时自动执行：

```csharp
var runtime = root.AddComponent<MResRuntimeBehaviour>();
runtime.Initialize(luaSource, metadata);
```

如果：

```text
manifest.lua == null
```

则不添加该组件。

`MResRuntimeBehaviour` 建议声明为内部 Runtime 组件，例如：

```csharp
using UnityEngine;
using XLua;

internal sealed class MResRuntimeBehaviour : MonoBehaviour
{
    private LuaTable _env;

    private LuaFunction _start;
    private LuaFunction _update;
    private LuaFunction _fixedUpdate;

    private LuaFunction _collisionEnter;
    private LuaFunction _collisionStay;
    private LuaFunction _collisionExit;

    private LuaFunction _triggerEnter;
    private LuaFunction _triggerStay;
    private LuaFunction _triggerExit;

    private LuaFunction _destroy;

    public void Initialize(
        string luaSource,
        MResMetadata metadata)
    {
        LuaEnv luaEnv = MResLuaRuntime.Env;

        _env = luaEnv.NewTable();

        LuaTable meta = luaEnv.NewTable();
        meta.Set("__index", luaEnv.Global);
        _env.SetMetaTable(meta);
        meta.Dispose();

        _env.Set(
            "self",
            new MResLuaObject(gameObject, metadata)
        );

        _env.Set("metadata", metadata);

        luaEnv.DoString(
            luaSource,
            $"MRes:{gameObject.name}",
            _env
        );

        _env.Get("Start", out _start);
        _env.Get("Update", out _update);
        _env.Get("FixedUpdate", out _fixedUpdate);

        _env.Get("OnCollisionEnter", out _collisionEnter);
        _env.Get("OnCollisionStay", out _collisionStay);
        _env.Get("OnCollisionExit", out _collisionExit);

        _env.Get("OnTriggerEnter", out _triggerEnter);
        _env.Get("OnTriggerStay", out _triggerStay);
        _env.Get("OnTriggerExit", out _triggerExit);

        _env.Get("OnDestroy", out _destroy);
    }
}
```

该组件的职责只有：

```text
执行 Package Lua
维护当前 MRes 实例独立 LuaTable
把 Unity 生命周期/物理事件转发给 xLua
释放 LuaFunction / LuaTable
```

它不属于被序列化的 Prefab 数据。

---

# 23. Unity Event -> xLua

至少支持：

```lua
Start()
Update(dt)
FixedUpdate(dt)

OnCollisionEnter(other)
OnCollisionStay(other)
OnCollisionExit(other)

OnTriggerEnter(other)
OnTriggerStay(other)
OnTriggerExit(other)

OnDestroy()
```

例如：

```csharp
private void Update()
{
    _update?.Call(Time.deltaTime);
}
```

碰撞：

```csharp
private void OnCollisionEnter(Collision collision)
{
    if (_collisionEnter == null)
        return;

    _collisionEnter.Call(
        new MResLuaObject(
            collision.gameObject,
            null
        )
    );
}
```

---

# 24. MResLuaObject

不要默认让脚本直接依赖：

```lua
CS.UnityEngine.*
```

应提供稳定 Wrapper：

```csharp
public sealed class MResLuaObject
{
    private readonly GameObject _gameObject;
    private readonly MResMetadata _metadata;

    public MResLuaObject(
        GameObject gameObject,
        MResMetadata metadata)
    {
        _gameObject = gameObject;
        _metadata = metadata;
    }

    public Vector3 GetPosition() { ... }

    public void SetPosition(float x, float y, float z) { ... }

    public void Translate(float x, float y, float z) { ... }

    public void Rotate(float x, float y, float z) { ... }

    public void AddForce(float x, float y, float z) { ... }

    public void SetVelocity(float x, float y, float z) { ... }

    public string GetName()
        => _metadata?.name;

    public string GetDescription()
        => _metadata?.description;

    public float GetValue()
        => _metadata?.value ?? 0;

    public string GetItemType()
        => _metadata?.type.ToString();
}
```

Lua：

```lua
function OnCollisionEnter(other)
    other:AddForce(0, 10, 0)
end
```

---

# 25. Metadata 在 Lua 中的使用

Lua 环境中同时暴露：

```lua
self
metadata
```

推荐行为 API 通过：

```lua
self:GetName()
self:GetDescription()
self:GetValue()
self:GetItemType()
```

获取。

也可以直接读取：

```lua
metadata.name
metadata.description
metadata.value
```

但 Wrapper API 为推荐方式。

---

# 26. Lua 示例：装置

Metadata：

```json
{
  "name": "弹射装置",
  "description": "碰撞后将目标向上弹射。",
  "value": 120,
  "type": "device"
}
```

Lua：

```lua
function OnCollisionEnter(other)
    other:AddForce(0, 10, 0)
end
```

---

# 27. Lua 示例：药物

Metadata：

```json
{
  "name": "实验药剂",
  "description": "用于触发特定药物交互效果。",
  "value": 50,
  "type": "medicine"
}
```

行为逻辑可以继续通过 Lua 定义，但具体角色生命值等游戏 API 不属于 MRes v1 核心，后续通过 Wrapper 扩展。

---

# 28. xLua 资源释放

`MResRuntimeBehaviour.OnDestroy()` 必须：

```text
调用 Lua OnDestroy
↓
Dispose 全部 LuaFunction
↓
Dispose LuaTable
↓
清空引用
```

**不能 Dispose 全局 LuaEnv。**

全局 LuaEnv 仅由：

```text
MResLuaRuntime
```

统一管理。

---

# 29. MResLoadHandle

动态创建的：

```text
Mesh
Texture2D
Material
PhysicsMaterial
GameObject
```

必须统一记录。

例如：

```csharp
public sealed class MResLoadHandle : IDisposable
{
    public GameObject Root { get; }
    public MResMetadata Metadata { get; }

    public void Dispose()
    {
        ...
    }
}
```

Dispose 时：

```text
Destroy Root
Destroy dynamic Material
Destroy dynamic PhysicsMaterial
Destroy dynamic Texture
Destroy dynamic Mesh
```

避免重复加载造成泄漏。

---

# 30. 推荐代码结构

```text
Assets/
  MRes/
    Runtime/
      Format/
        MResFormat.cs
        MResReader.cs
        MResEntry.cs

      Loading/
        MResLoader.cs
        MResLoadHandle.cs
        MResPackage.cs

      Prefab/
        MResPrefabData.cs
        MResPrefabBuilder.cs

      Resources/
        MResMeshReader.cs
        MResTextureReader.cs
        MResMaterialReader.cs
        MResPhysicsMaterialReader.cs

      Shader/
        MResShaderRegistry.cs

      Lua/
        MResLuaRuntime.cs
        MResLuaRuntimeDriver.cs
        MResRuntimeBehaviour.cs
        MResLuaObject.cs

      Metadata/
        MResMetadata.cs
        MResItemType.cs

      Exceptions/
        MResFormatException.cs
        MResShaderException.cs
        MResLuaException.cs
```

Runtime Assembly 必须引用 xLua Assembly。

禁止引用 `UnityEditor`。

---

# 31. 动态加载示例

```csharp
public class MResSpawner : MonoBehaviour
{
    public string path;

    private MResLoadHandle handle;

    private void Start()
    {
        handle = MResLoader.Load(path);

        handle.Root.transform.position =
            new Vector3(0, 1, 0);
    }

    private void OnDestroy()
    {
        handle?.Dispose();
    }
}
```

---

# 32. 验收测试

### Test A：byte[] 加载

```csharp
MResLoader.Load(bytes);
```

不得创建任何临时磁盘文件。

### Test B：Hierarchy

多级父子关系正确。

### Test C：Mesh

顶点、法线、UV、SubMesh 正确。

### Test D：Material

shaderId、Texture、参数正确。

### Test E：Collider

Box / Sphere / Capsule / MeshCollider 的形状参数与公共 Collider 属性正确恢复。

### Test F：PhysicsMaterial

`dynamicFriction / staticFriction / bounciness / frictionCombine / bounceCombine` 正确恢复；多个 Collider 引用同一路径时共享同一个 PhysicsMaterial。

### Test G：Rigidbody

Rigidbody 参数正确恢复.

### Test H：Metadata

```text
name
description
value
type
```

正确解析。

### Test I：非法 Metadata Type

除：

```text
medicine
device
```

外全部拒绝加载。

### Test J：xLua 自动挂载

源 Prefab Schema 中不存在任何 Lua Behaviour。

当 `manifest.lua` 存在时，Loader 必须自动在 Root 上添加内部 `MResRuntimeBehaviour`。

```lua
function Start()
    print("started")
end
```

能够执行。

### Test K：xLua Collision

```lua
function OnCollisionEnter(other)
    other:AddForce(0, 10, 0)
end
```

能实际影响碰撞目标 Rigidbody。

### Test L：Lua Environment 隔离

加载两个相同 `.mres` 实例。

各实例 Lua 局部变量不得互相污染。

### Test M：LuaEnv 共享

加载多个 MRes 时不得创建多个 LuaEnv。

### Test N：TOC 越界

修改某个 Entry 的 `Offset` 或 `Size` 使其超出文件范围后，Loader 必须拒绝加载。

### Test O：无 UnityEditor

Runtime Assembly 不得引用 UnityEditor。

---

# 33. 完成定义

Codex 最终交付：

- 独立 MRes Runtime Loader。
- MRes v1 Reader。
- Header / TOC 结构校验。
- Mesh / Texture / Material / PhysicsMaterial 动态重建。
- Shader Registry。
- Prefab Hierarchy 动态重建。
- Collider / Rigidbody 恢复。
- xLua 集成。
- 单一共享 LuaEnv。
- 内部 MResRuntimeBehaviour（由 Loader 自动挂到 Root）。
- MResLuaObject Wrapper。
- 固定 Metadata Schema。
- `MResLoadHandle` 资源释放机制。
- 测试场景。
- README。

**不要实现 Packer。**
