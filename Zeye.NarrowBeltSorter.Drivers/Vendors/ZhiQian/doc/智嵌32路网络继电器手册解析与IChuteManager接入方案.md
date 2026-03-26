# 智嵌 32 路网络继电器控制器手册解析与 `IChuteManager` 接入方案

## 1. 文档目的与范围

- 本文基于 `【智嵌物联】32路网络继电器控制器用户使用手册V1.2.pdf` 提取并整理：
  - 如何连接设备；
  - 如何开闭 IO；
  - IO 如何定义；
  - 如何透传；
  - 需要如何配置；
  - 如何使用工具测试；
  - 测试示例；
  - 后续如何接入到项目 `IChuteManager` 实现。
- 本文仅引用手册可核对内容，并给出面向本仓库的工程化落地建议。

## 2. 设备能力与边界（手册摘要）

### 2.1 基本能力

- 设备为 32 路继电器输出控制板，带 1 路 RJ45 与 1 路 RS485，可通过 Modbus TCP/RTU、自定义协议、ASCII 协议控制（手册第 2.1 节，第 7 章）。
- 支持串口服务器能力（网络与 RS485 数据互转）、支持 Modbus TCP 转 RTU（手册第 2.1 节、第 6.2 节、第 6.5 节）。
- 默认网络参数：`IP=192.168.1.253`、`端口=1030`、工作模式 `TCP_SERVER`、RS485 `115200 8N1`（手册表 2.1）。

### 2.2 型号边界说明

- 手册适配多个型号，示例以 `ZQWL-IO-1BZRC32` 为主（手册第 1.2 节）。
- 表 1.1 对示例型号标注“无 DI 输入”，但第 7 章协议仍给出了 DI 读取/计数命令，属于系列协议统一定义。项目接入时应以实物型号能力为准（手册第 1.2 节、第 7.2.4~7.2.7 节、第 7.3 节）。

## 3. 如何连接（网络/供电/拓扑）

### 3.1 单机直连调试（最小闭环）

1. 电脑网口与设备网口直连（或同交换机），设备上电。
2. 电脑 IP 与设备 IP 保持同网段（默认 192.168.1.xxx 网段）。
3. 观察指示灯：PWR 常亮、RUN 闪烁、网口灯一常亮一闪烁（手册第 2.3 节、第 4.3 节）。

### 3.2 接线与触点

- 每路继电器有常开/常闭/公共端三个触点，32 路公共端相互独立，可分别控制不同电压负载（手册第 4.2 节）。
- Y1~Y32 指示灯亮灭分别对应继电器常开触点与公共端闭合/断开（手册第 4.3 节）。

### 3.3 级联/联网拓扑

- 支持“网络 IO + RS485 IO”级联，手册给出最多可级联 32 个 RS485 设备的拓扑示意（手册第 6.2 节）。
- 设备间一对一联动示例中，A 设为 TCP_SERVER，B 设为 TCP_CLIENT，B 的目标 IP/端口指向 A（手册第 9.2 节）。

## 4. 如何开闭 IO（DO 控制）

### 4.1 配置软件方式（人工调试）

- 在官方配置软件进入“IO 控制”页面，可直接单击 Y1~Y32 切换状态；支持“全部打开/全部关闭”；可设置轮询间隔（默认 200ms）（手册第 2.3 节）。

### 4.2 ASCII 协议方式（文本指令）

- 全量 DO 设置：
  - 帧：`zq <addr> set <32路状态> qz`（状态 0 断开 /1 闭合 /2 翻转 /3 不动作）。
- 单路 DO 设置：
  - 帧：`zq <addr> set y01~y32 <state> qz`。
- 单路延时断开：
  - 帧：`zq <addr> set yxx <state> <delayMs> qz`，`delayMs` 范围 `0~2147483647`（手册第 7.2.1~7.2.3 节）。

### 4.3 自定义 15 字节协议方式（二进制）

- 集中控制命令 `0x57` 可一帧写全量继电器状态（15 字节帧）。
- 单路控制命令 `0x70` 可单路开闭并带延时字节（10 字节帧）（手册第 7.1.1 节）。

### 4.4 Modbus 方式（工程接入推荐）

- RTU/TCP 功能码均支持：`0x01/0x02/0x03/0x05/0x06/0x0F/0x10`（手册表 7.8、第 7.5 节）。
- 对 DO 常用两类写法：
  - `0x05` 写单线圈（单路）；
  - `0x0F` 写多线圈（批量）；
  - 或通过保持寄存器区写 DO 状态与延时（`0x10`）（手册第 7.4 节）。

## 5. IO 如何定义（地址/语义）

### 5.1 逻辑命名

- DO：`Y1~Y32`；
- DI：`X1~X32`（协议定义存在，具体是否有 DI 取决于型号硬件）（手册第 7.2.4~7.2.5 节、第 7.4 节）。

### 5.2 状态语义

- DO 状态：
  - `0`：断开（常开断开，常闭闭合）；
  - `1`：闭合（常开闭合，常闭断开）；
  - 部分协议中还支持“翻转/保持原状态”语义（手册第 7.1.1 节、第 7.2 节）。
- DI 状态：
  - `0`：无有效信号；
  - `1`：有有效信号（手册第 7.2.4 节、第 7.4 节）。

### 5.3 Modbus 地址定义（关键区）

- 设备信息区起始：`0x0000`（地址、波特率、校验位、版本号、恢复出厂、复位）。
- DI/DO 状态区起始：`0x1000`：
  - `0x1000 + 0x00~0x3F`：DI；
  - `0x1000 + 0x40~0x7F`：DO。
- DI 脉冲计数区起始：`0x10A0`（每路 4 字节）。
- DO 延时通断区起始：`0x11A0`（状态 + 延时毫秒）（手册表 7.9）。

## 6. 如何透传（串口服务器/协议转换）

- 设备处理链路（手册图 6.4）：
  1. 接收到数据后先判断是否满足“控制协议 + 地址匹配”；
  2. 若满足：设备执行控制动作；
  3. 若不满足：按透传处理（网络⇄RS485 转发）。
- 当启用“Modbus TCP 转 RTU”时，工作模式必须是 `TCP_SERVER`（手册第 5.2 节）。
- 故障排查中明确了两种运行形态：
  - 未勾选 Modbus TCP 转 RTU：透明传输；
  - 勾选后：协议转换，收发必须满足协议格式（手册“常见故障处理”）。

## 7. 需要如何配置（上线前最小参数集）

### 7.1 必配项

- 网络：IP/掩码/网关/端口、静态或动态 IP。
- 串口：波特率/数据位/校验位/停止位。
- 工作模式：`TCP_SERVER` / `TCP_CLIENT` / `UDP_SERVER` / `UDP_CLIENT` 四选一。
- 可选：目标地址/端口（Client 模式需要）、DNS、心跳包、注册包（手册第 5.1~5.2 节、第 6.1 节）。

### 7.2 生效规则

- 参数保存后必须重启设备，配置才生效（手册第 5.1、5.2 节）。
- 忘记参数可通过 `RESET` 长按 5 秒恢复出厂：`IP=192.168.1.253`、串口 `115200 8N1`、地址 `1`（手册第 8.1 节）。

### 7.3 配置限制与边界条件（易踩坑清单）

- 地址范围：
  - ASCII/自定义协议地址范围为 `0~255`；
  - `255`（或 `0xFF`）属于广播地址，手册明确“并非所有命令都可广播”，例如自定义配置指令里仅“读控制板参数”可用广播地址（手册第 7.1.2 节、第 7.2 节）。
- 工作模式限制：
  - 工作模式仅可四选一：`TCP_SERVER / TCP_CLIENT / UDP_SERVER / UDP_CLIENT`（手册第 5.2 节）。
  - 启用“Modbus TCP 转 RTU”时，必须选 `TCP_SERVER`（手册第 5.2 节）。
- 目标地址/端口生效条件：
  - 仅在 `TCP_CLIENT / UDP_CLIENT` 模式下有意义；
  - 在 `TCP_SERVER / UDP_SERVER` 下无意义（手册第 5.2 节）。
- 串口参数限制：
  - 波特率支持 `600~460800bps`（手册第 5.1 节）；
  - 默认为 `115200 8N1`（手册表 2.1、8.1 节）。
- 单路控制范围：
  - 单路控制序号为 `1~32`（自定义协议 Byte5 范围 `0x01~0x20`，手册第 7.1.1.2 节）。
- 延时参数范围：
  - ASCII 延时断开时间范围 `0~2147483647 ms`（手册第 7.2.3 节）。
- 配置生效机制：
  - 任何参数变更后都必须保存并重启，未重启时现场仍按旧参数运行（手册第 5.1、5.2 节、常见故障处理）。

### 7.4 设备网页配置（详细步骤）

以下步骤对应手册第 5.2 节“网页参数配置”，用于替代或补充配置软件操作。

#### 7.4.1 登录网页

1. 在浏览器输入设备 IP（默认 `192.168.1.253`）。
2. 输入用户名/密码（默认均为 `admin`）。
3. 登录后进入左侧菜单配置页。

> 若忘记 IP，可按住 `RESET` 超过 5 秒恢复出厂，再按默认 IP 登录（手册第 5.2 节、第 8.1 节）。

#### 7.4.2 模块 IP 配置页面（网络参数）

页面入口：左侧 `模块 IP 配置`。  
可配置项（手册第 5.2 节）：

- 模块地址与网络参数：IP、子网掩码、网关；
- 网页访问端口；
- 是否自动获取 IP（动态 IP）。

操作建议：

1. 先确认现场网络规划（静态还是动态 IP）。
2. 若静态 IP，避免与现网冲突；保证与上位机可达。
3. 提交配置后务必重启设备，再做连通性测试（ping + 业务连接）。

#### 7.4.3 USART 配置页面（串口/工作模式）

页面入口：左侧 `USART 配置`（即 RS485 参数页）。  
可配置项（手册第 5.2 节）：

- 串口参数：波特率、数据位、停止位、校验位；
- 工作模式：`TCP_SERVER / TCP_CLIENT / UDP_SERVER / UDP_CLIENT`；
- 目标地址、目标端口（仅 Client 模式有意义）；
- 注册/心跳包相关设置（TCP_CLIENT 常用）。

关键限制（手册明确）：

- 选择 `TCP_SERVER` 或 `UDP_SERVER` 时，目标地址/端口无意义；
- 选择 `TCP_CLIENT` 或 `UDP_CLIENT` 时，目标地址/端口必须填写且可达；
- 启用 `Modbus TCP 转 RTU` 时，工作模式必须选 `TCP_SERVER`；
- 注册心跳包时间为 0 时，表示禁用该功能。

#### 7.4.4 密码管理与其他页面

根据手册第 5.2 节，可继续在网页配置：

- 密码管理（修改登录密码）；
- 产品信息；
- 重启设备；
- 系统登录相关项。

注意事项：

- 用户名修改主要通过配置软件执行（手册第 5.1 节说明），网页侧重点在参数和密码管理；
- 修改完成后建议立即记录资产台账，避免现场遗忘账号。

#### 7.4.5 网页配置后的最小验收流程（建议）

1. 重新登录网页，确认参数已保存；
2. 使用网络调试助手连接目标端口；
3. 执行 Y1/Y2 开闭测试（参考 9.1.1、9.1.2）；
4. 回读全量 DO 状态（`get y`）；
5. 若启用了 Modbus TCP 转 RTU，再做一次 Modbus 读线圈验证。

#### 7.4.6 网页配置常见错误与对应处理

- **现象：网页参数改了但控制仍失败**  
  原因：未重启设备导致新参数未生效。  
  处理：保存后重启，再复测（手册第 5.2 节、常见故障处理）。

- **现象：TCP_CLIENT 模式一直连不上**  
  原因：目标 IP/端口错误，或目标服务未监听。  
  处理：核对目标地址、端口、路由与防火墙，抓包确认三次握手。

- **现象：勾选 Modbus TCP 转 RTU 后通信异常**  
  原因：模式未设为 `TCP_SERVER`，或收发报文不符合协议。  
  处理：切换至 `TCP_SERVER`，并核对 Modbus 报文格式（手册第 5.2 节、常见故障处理）。

## 8. 如何使用工具测试（建议流程）

### 8.1 官方配置软件联调

1. 选择正确网卡；
2. 搜索设备；
3. 进入 IO 控制页；
4. 观察 Y1~Y32 状态变化与现场负载动作；
5. 必要时打开“显示扫描命令”查看交互数据（手册第 2.3 节）。

### 8.2 网络调试助手联调

1. 选择 `TCP Client`；
2. 填入设备 IP（默认 `192.168.1.253`）和端口（默认 `1030`）；
3. 连接成功后发送 ASCII 或十六进制指令（手册第 2.4 节、第 7 章）。

### 8.3 回归检查点

- 控制后回读 DO 状态（ASCII `get y` 或 Modbus `0x01/寄存器`）。
- 若有 DI 能力，回读 DI 状态与脉冲计数（手册第 7.2.4~7.2.7 节）。
- 修改参数后必须重启再复测（手册第 5 章、常见故障处理）。

## 9. 测试示例（可直接复用）

### 9.1 ASCII 示例

- 全部闭合（地址 1）：
  - `zq 1 set 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1 qz`
- 单路闭合（Y02）：
  - `zq 1 set y02 1 qz`
- 单路闭合 5 秒后断开（Y02）：
  - `zq 1 set y02 1 5000 qz`
- 回读全量 DO：
  - `zq 1 get y qz`

以上均来自手册第 7.2 节示例格式。

### 9.1.1 开闭第 1 路 IO（Y01）分步示例

- 打开第 1 路：
  - 发送：`zq 1 set y01 1 qz`
  - 期望应答：`zq 1 ret y01 1 qz`
- 关闭第 1 路：
  - 发送：`zq 1 set y01 0 qz`
  - 期望应答：`zq 1 ret y01 0 qz`
- 打开 5 秒后自动关闭：
  - 发送：`zq 1 set y01 1 5000 qz`
  - 期望应答：`zq 1 ret y01 1 5000 qz`

### 9.1.2 开闭第 2 路 IO（Y02）分步示例

- 打开第 2 路：
  - 发送：`zq 1 set y02 1 qz`
  - 期望应答：`zq 1 ret y02 1 qz`
- 关闭第 2 路：
  - 发送：`zq 1 set y02 0 qz`
  - 期望应答：`zq 1 ret y02 0 qz`
- 查询第 2 路当前状态与剩余延时：
  - 发送：`zq 1 get y02 qz`
  - 应答示例：`zq 1 ret y02 1 5000 qz`

### 9.2 自定义二进制示例

- 地址 1，写继电器状态（示例中第 1 路与第 9 路闭合）：
  - `48 3A 01 57 01 00 01 00 00 00 00 00 DC 45 44`
- 地址 1，第 1 路闭合：
  - `48 3A 01 70 01 01 00 00 45 44`

以上来自手册第 7.1.1 节示例。

### 9.2.1 开闭第 1 路与第 2 路（自定义单路命令）

- 第 1 路闭合：
  - `48 3A 01 70 01 01 00 00 45 44`
- 第 1 路断开：
  - `48 3A 01 70 01 00 00 00 45 44`
- 第 2 路闭合：
  - `48 3A 01 70 02 01 00 00 45 44`
- 第 2 路断开：
  - `48 3A 01 70 02 00 00 00 45 44`

说明：`Byte5` 是继电器序号，`0x01` 对应第 1 路，`0x02` 对应第 2 路；`Byte6=0x01` 为闭合、`0x00` 为断开（手册第 7.1.1.2 节）。

### 9.3 Modbus 示例（手册示例框架）

- 读线圈：功能码 `0x01`；
- 写单线圈：功能码 `0x05`；
- 写多线圈：功能码 `0x0F`；
- 写寄存器：功能码 `0x10`（可用于 DO/延时/计数相关区）。

请求/应答字段可按手册第 7.4、7.5 节模板构造。

### 9.3.1 Modbus 写单线圈：第 1 路/第 2 路开闭

- 地址：假定从站地址 `0x01`。
- 线圈地址映射：`0x0000` 对应 Y1，`0x0001` 对应 Y2（手册第 7.4 节“写单个线圈”起始地址范围说明）。
- 写第 1 路闭合（Y1=ON）：
  - 功能码 `0x05`，起始地址低字节 `0x00`，线圈状态高字节 `0xFF`。
- 写第 1 路断开（Y1=OFF）：
  - 功能码 `0x05`，起始地址低字节 `0x00`，线圈状态高字节 `0x00`。
- 写第 2 路闭合（Y2=ON）：
  - 功能码 `0x05`，起始地址低字节 `0x01`，线圈状态高字节 `0xFF`。
- 写第 2 路断开（Y2=OFF）：
  - 功能码 `0x05`，起始地址低字节 `0x01`，线圈状态高字节 `0x00`。

> CRC16 与 MBAP 报文头按现场协议栈自动计算，建议直接用 TouchSocket.Modbus API 组帧，避免手工计算误差。

### 9.3.2 Modbus 批量写第 1 路与第 2 路（高性能优先）

- 使用功能码 `0x0F` 一次写多个线圈；
- 例如同一帧内设置：Y1=ON、Y2=OFF，其他路保持既定策略；
- 优势：相比逐路 `0x05` 可减少报文往返次数，更适合高频联动场景。

## 9.4 配置软件实操示例（第 1 路/第 2 路）

1. 打开智嵌配置软件并搜索设备；
2. 双击设备进入 IO 控制页；
3. 单击 Y1，观察 Y1 指示灯与负载动作；
4. 单击 Y2，观察 Y2 指示灯与负载动作；
5. 点击“全部关闭”，再逐个打开 Y1、Y2 做回归；
6. 需要抓包时勾选“显示扫描命令”，完成后建议关闭以避免干扰手工调试。

## 9.5 高性能读写建议（工程实践）

以下为在手册协议能力基础上的工程化实践建议，用于提升吞吐与稳定性：

- 批量优先：
  - 写：优先 `0x0F` 批量写线圈，而不是多次 `0x05`；
  - 读：优先一次读取全量线圈/寄存器，而不是逐点读取（手册第 7.4 节已建议“尽量一次读取全部状态”）。
- 长连接优先：
  - TCP 模式下保持长连接，减少频繁建连带来的时延与抖动。
- 读写节奏控制：
  - 控制命令与状态轮询分离队列；
  - 轮询周期建议从 100~200ms 起步，按现场网络质量逐步优化（手册配置软件默认扫描间隔 200ms，可作为初始参考）。
- 写后读校验：
  - 每次关键写入后立即回读目标位，若连续 N 次不一致再重试并告警。
- 避免广播误控：
  - 除明确允许的广播读取外，不在运行态使用广播写控制。
- 参数变更窗口化：
  - 网络与串口参数修改统一安排维护窗口，保存后重启并做最小回归（Y1/Y2 开闭 + 全量状态回读）。

## 10. 后续接入项目 `IChuteManager` 的实现建议

### 10.1 接入目标

- 将智嵌 32 路继电器板作为“格口执行器”驱动，实现：
  - `SetForcedChuteAsync` / `SetChuteLockedAsync` / `AddTargetChuteAsync` 等管理命令到 DO 控制命令的映射；
  - 连接状态与故障状态上抛到 `ConnectionStatusChanged` / `Faulted` 事件。

### 10.2 分层建议（保持低侵入）

- 建议在 `Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/` 新增智嵌驱动实现，参考现有 `LeiMa` 厂商目录分层。
- `Core` 仅保留接口与事件契约，不引入厂商协议细节。
- 协议适配建议优先走 Modbus（便于复用 TouchSocket.Modbus 与现有通信栈）。

### 10.3 代码接入总流程（非常具体）

建议按以下 8 个阶段推进，每一阶段都可单独联调验收：

1. **配置建模阶段**：先定义智嵌专用 Options（连接、协议、轮询、重试、地址映射）。
2. **通信适配阶段**：封装“写 DO / 读 DO / 读 DI / 写延时”最小能力接口。
3. **驱动实现阶段**：实现 `ZhiQianChuteManager`（`IChuteManager` 的智嵌实现）。
4. **状态模型阶段**：建立 ChuteId ↔ Y路 的映射缓存与快照输出。
5. **事件发布阶段**：打通 `ForcedChuteChanged`、`ChuteLockStatusChanged`、`ConnectionStatusChanged`、`Faulted`。
6. **DI 注入阶段**：在 Host 中按配置切换 Vendor，实现零入侵替换。
7. **回归测试阶段**：单元测试（映射/边界/异常）+ 集成联调（现场板卡）。
8. **上线保护阶段**：灰度开启、写后读校验、失败重试与自动降级。

### 10.4 目录与文件落地建议（到文件级）

建议新增如下文件（示意）：

- `Zeye.NarrowBeltSorter.Core/Options/Chutes/ZhiQianChuteOptions.cs`  
  - 智嵌接入配置对象（地址映射、轮询周期、超时、重试参数）。
- `Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianModbusClientAdapter.cs`  
  - 仅负责与设备通讯：读写线圈、读写寄存器、连接管理。
- `Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianChuteManager.cs`  
  - `IChuteManager` 具体实现，完成业务语义映射与事件发布。
- `Zeye.NarrowBeltSorter.Drivers/Vendors/ZhiQian/ZhiQianAddressMap.cs`  
  - 统一管理 Y 路、线圈地址、寄存器地址换算，避免重复代码。
- `Zeye.NarrowBeltSorter.Core.Tests/ZhiQianChuteManagerTests.cs`  
  - 覆盖 `IChuteManager` 契约语义（返回值、事件、异常隔离）。

> 命名建议尽量使用领域术语，避免 `Class1`、`Helper2` 这类无语义命名。

### 10.5 Options 设计建议（字段级）

`ZhiQianChuteOptions` 建议至少包含：

- `Enabled`：是否启用智嵌驱动；
- `Transport`：`ModbusTcp` / `ModbusRtu`；
- `Host`、`Port`：TCP 连接参数；
- `SerialPortName`、`BaudRate`、`DataBits`、`Parity`、`StopBits`：RTU 参数；
- `DeviceAddress`：设备站号；
- `CommandTimeoutMs`：单次命令超时；
- `RetryCount`、`RetryDelayMs`：重试策略（建议用 Polly）；
- `PollIntervalMs`：状态轮询周期；
- `EnableWriteBackVerify`：是否启用写后读校验；
- `ChuteToDoMap`：`Dictionary<long, int>`，定义 `ChuteId -> Y1~Y32` 映射；
- `DefaultOpenDurationMs`：默认开闸时长（用于无明确时窗场景）。

参数校验建议：

- `PollIntervalMs` 不小于 50ms；
- `CommandTimeoutMs` 不小于 100ms；
- `ChuteToDoMap` 不允许重复 Y 路，不允许超出 1~32；
- `DeviceAddress` 按协议范围校验；
- 所有校验失败统一记录日志并拒绝启动。

### 10.6 通信适配接口建议（最小高复用）

建议先抽象一个最小接口，避免 `IChuteManager` 直接依赖底层协议细节：

- `ConnectAsync` / `DisconnectAsync`
- `ReadDoStatesAsync()`：一次回读 32 路 DO
- `WriteSingleDoAsync(int doIndex, bool isOn)`
- `WriteBatchDoAsync(IReadOnlyDictionary<int, bool> doStates)`
- `WriteDoWithDelayAsync(int doIndex, bool isOn, int delayMs)`

实现要点：

- 优先用批量读写（`0x01`/`0x0F`），减少往返次数；
- 同一连接复用，避免频繁重建连接；
- 异常统一抛给上层，由 `SafeExecutor` 兜底隔离。

### 10.7 `IChuteManager` 方法到设备命令映射（逐方法）

#### 10.7.1 `ConnectAsync`

- 步骤：
  1. 校验 Options；
  2. 建立通信连接；
  3. 首次读取 DO 全量状态并构建快照；
  4. 设置 `ConnectionStatus` 并发布 `ConnectionStatusChanged`。

#### 10.7.2 `DisconnectAsync`

- 停止轮询；
- 关闭连接；
- 状态切换为 `Disconnected`；
- 发布连接状态变更事件。

#### 10.7.3 `SetChuteLockedAsync(chuteId, isLocked)`

- 查 `ChuteToDoMap` 得到目标 Y 路；
- `isLocked=true`：建议将目标路置为断开（防止误开）；
- `isLocked=false`：恢复可控，不主动开闸；
- 成功后更新 `LockedChuteIds` 并发布 `ChuteLockStatusChanged`。

#### 10.7.4 `SetForcedChuteAsync(chuteId)`

- `chuteId==null`：清除强排状态，不主动改 DO（或按业务配置执行复位策略）；
- `chuteId!=null`：映射到目标 Y 路，执行打开动作；
- 可选：将其他非目标路按策略关闭，防止并发落错；
- 更新 `ForcedChuteId` 并发布 `ForcedChuteChanged`。

#### 10.7.5 `AddTargetChuteAsync / RemoveTargetChuteAsync`

- 仅更新目标集合与配置快照；
- 是否立刻写 DO 由业务策略决定（推荐“不立即动作”，仅影响后续路由决策）。

#### 10.7.6 `TryGetChute`

- 从内存快照中返回 `IChute` 视图；
- 不触发设备通信，保证高频调用性能。

### 10.8 事件与状态一致性（避免竞态）

- 所有状态更新先落内存快照，再发事件；
- 事件发布时间使用单线程串行队列，防止乱序；
- 对同一 `chuteId` 的开闭命令使用细粒度锁（如 `SemaphoreSlim`）避免并发覆盖；
- 轮询回读与业务写命令要有版本号，防止旧回读覆盖新写入状态。

### 10.9 异常、日志与安全执行（必须项）

- 所有危险路径（IO 写操作）必须通过统一 `SafeExecutor` 执行；
- catch 到异常后必须记录日志，并：
  - 方法返回 `false`；
  - 触发 `Faulted` 事件（包含操作名、设备地址、异常摘要）。
- 日志建议分级：
  - `Information`：连接、断开、关键写入成功；
  - `Warning`：重试、部分失败、写后读不一致；
  - `Error`：连接失败、连续重试失败、配置非法。

### 10.10 重试与性能策略（可直接落地）

- 重试策略：统一使用 Polly，建议“短延时有限重试”：
  - 读命令：重试 1~2 次；
  - 写命令：重试 2~3 次（含写后读校验失败场景）；
- 批量优先：
  - 单次业务需改多路 DO 时，合并成一次 `0x0F`；
- 轮询分层：
  - 快速轮询 DO（例如 100~200ms）；
  - 慢速轮询 DI/计数（例如 500~1000ms）；
- 命令排队：
  - 采用单写多读队列模型，避免并发写导致抖动。

### 10.11 Host 层 DI 接入步骤（按现有结构）

在 `Program.cs` 建议采用“按配置选择厂商实现”：

1. 读取 `Chutes:Vendor` 配置；
2. 若 `Vendor == ZhiQian`，注册 `IChuteManager -> ZhiQianChuteManager`；
3. 同时注册 `ZhiQianModbusClientAdapter` 与 `ZhiQianChuteOptions`；
4. 保持其他 Vendor 注册逻辑不变，实现最小侵入切换。

这样可在不修改上层业务调用代码的前提下完成驱动替换。

### 10.12 最小可运行里程碑（建议）

- **M1（1~2 天）**：建连 + Y1/Y2 开闭 + 状态回读；
- **M2（2~3 天）**：`SetForcedChuteAsync`、`SetChuteLockedAsync` 全链路；
- **M3（2~3 天）**：事件与故障上报 + 重试 + 写后读校验；
- **M4（1~2 天）**：压测与现场联调（并发命令、网络抖动、断线恢复）。

### 10.13 验收用例清单（建议直接照此执行）

1. 连接成功后 `ConnectionStatus=Connected`；
2. 断开后状态回到 `Disconnected`；
3. 开闭第 1 路/第 2 路命令都返回成功并与回读一致；
4. 配置非法（映射越界/重复）时拒绝启动并记录错误日志；
5. 人为断网后自动重连成功，且事件顺序正确；
6. 连续高频命令下无错路动作（无“第 1 路命令误改第 2 路”）。

### 10.14 `IChuteManager` 映射草案（简版总览）

- 连接态：
  - 驱动建连成功 -> `DeviceConnectionStatus.Connected`；
  - 断连/失败 -> `Disconnected/Faulted` 并触发 `Faulted` 事件。
- 单格口锁定：
  - `SetChuteLockedAsync(chuteId, true)` -> 对应 Y 路输出断开（或按业务定义保持关闭）；
  - `SetChuteLockedAsync(chuteId, false)` -> 恢复可控。
- 强排口：
  - `SetForcedChuteAsync(chuteId)` -> 目标 Y 路置开，其他路按策略关断或保持；
  - 清空强排 -> 还原到目标口策略。
- 时窗控制：
  - 利用 DO 延时断开能力（ASCII 延时或 Modbus `0x11A0` 区）映射 `OpenAt/CloseAt` 行为。

### 10.15 可靠性与可观测性建议

- 危险 IO 执行路径统一经 `SafeExecutor` 包裹，异常必须记录日志并转化为 `false/事件`。
- 采用“写后读”校验（写 DO 后立即回读），降低现场误动作风险。
- 对广播地址（255/0xFF）仅用于手册明确允许场景（如读参数），常规控制禁用广播，避免误控。

### 10.16 格口绑定配置项清单（可直接落地）

以下配置项用于回答“哪些配置要绑定格口”这一核心问题，建议全部放在  
`Zeye.NarrowBeltSorter.Core/Options/Chutes/ZhiQianChuteOptions.cs`：

| 配置项 | 类型 | 必填 | 作用 | 边界规则 |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | 是 | 是否启用智嵌驱动 | `false` 时不注册该驱动 |
| `Transport` | `string` | 是 | 协议选择：`ModbusTcp`/`ModbusRtu` | 仅允许枚举值 |
| `Host` | `string` | TCP 必填 | 设备 IP | 非空、合法地址 |
| `Port` | `int` | TCP 必填 | 设备端口 | 1~65535 |
| `SerialPortName` | `string` | RTU 必填 | 串口名称 | 非空 |
| `BaudRate` | `int` | RTU 必填 | 波特率 | 与网页配置一致 |
| `DeviceAddress` | `byte` | 是 | 站号/从站地址 | 按手册范围校验 |
| `CommandTimeoutMs` | `int` | 是 | 单命令超时 | `>=100` |
| `RetryCount` | `int` | 是 | 重试次数 | 建议 0~5 |
| `RetryDelayMs` | `int` | 是 | 重试间隔 | `>=10` |
| `PollIntervalMs` | `int` | 是 | 状态轮询周期 | `>=50` |
| `EnableWriteBackVerify` | `bool` | 是 | 写后读校验开关 | 建议默认 `true` |
| `DefaultOpenDurationMs` | `int` | 是 | 默认开闸持续时长 | `>=20` |
| `ChuteToDoMap` | `Dictionary<long,int>` | 是 | **格口绑定关系：`chuteId -> Y1~Y32`** | chuteId 唯一、Y 路唯一且范围 1~32 |
| `ForceOpenExclusive` | `bool` | 否 | 强排是否独占（关掉其他路） | 默认 `true` 更安全 |

> 最关键映射只有一个：`ChuteToDoMap`。其余配置都是在保障映射“稳定可控”。

### 10.17 格口绑定示例（配置片段）

```json
{
  "Chutes": {
    "Vendor": "ZhiQian",
    "ZhiQian": {
      "Enabled": true,
      "Transport": "ModbusTcp",
      "Host": "192.168.1.199",
      "Port": 502,
      "DeviceAddress": 1,
      "CommandTimeoutMs": 300,
      "RetryCount": 2,
      "RetryDelayMs": 50,
      "PollIntervalMs": 100,
      "EnableWriteBackVerify": true,
      "DefaultOpenDurationMs": 120,
      "ForceOpenExclusive": true,
      "ChuteToDoMap": {
        "101": 1,
        "102": 2,
        "103": 3
      }
    }
  }
}
```

说明：

- `101/102/103` 是业务格口 Id；
- `1/2/3` 是继电器板的 Y 路；
- 同一时刻通过 `ChuteToDoMap` 完成业务语义与硬件地址绑定，避免在业务代码中散落硬编码。

### 10.18 控制流程（从 IChuteManager 到板卡）

建议统一控制链路如下：

1. 上层调用 `IChuteManager`（如 `SetForcedChuteAsync(101)`）。
2. `ZhiQianChuteManager` 用 `ChuteToDoMap` 解析出 `Y01`。
3. 进入 `SafeExecutor` 危险执行区。
4. 调用 `ZhiQianModbusClientAdapter.WriteSingleDoAsync(1, true)`。
5. 若 `EnableWriteBackVerify=true`，立即回读 32 路 DO 并校验 `Y01=true`。
6. 成功后更新内存快照并发布 `ForcedChuteChanged`。
7. 失败则按 Polly 重试；仍失败时记录错误日志并发布 `Faulted`。

建议控制策略：

- 单路动作：优先 `0x05`（写单线圈）；
- 多路联动：合并为 `0x0F`（写多线圈）；
- 对“强排独占”场景，先批量写关闭其他路，再写目标路打开，确保顺序一致。

### 10.19 需要定义的核心内容（接口/模型/事件）

为完整接入 `IChuteManager`，建议至少定义以下内容：

1. **Options**
   - `ZhiQianChuteOptions`
   - （可选）`ZhiQianProtocolOptions`（若后续扩展 ASCII/二进制并行支持）
2. **地址映射模型**
   - `ZhiQianAddressMap`（集中做 Y 路与线圈地址换算）
3. **通信接口**
   - `IZhiQianModbusClientAdapter`
4. **驱动实现**
   - `ZhiQianModbusClientAdapter`（TouchSocket.Modbus + Polly）
   - `ZhiQianChuteManager`（实现 `IChuteManager`）
5. **事件载荷（若现有事件不足）**
   - `ZhiQianChuteWriteVerifiedEventArgs`（写后读校验结果）
   - `ZhiQianChuteCommandRetriedEventArgs`（重试明细）
6. **测试**
   - 映射合法性测试（重复 Y 路、越界、空映射）
   - 方法语义测试（`Connect/Disconnect/SetForced/SetLocked`）
   - 异常隔离测试（`SafeExecutor` 返回 false + `Faulted` 事件）

### 10.20 相关读写示例代码（仅文档示例，不直接落库）

以下示例用于说明“如何读写智嵌 32 路继电器”，便于后续实现时直接参考。

#### 10.20.1 最小通信接口示例（读 DO / 写单路 / 批量写）

```csharp
using TouchSocket.Modbus;

/// <summary>
/// 智嵌继电器最小读写接口（示例）。
/// </summary>
public interface IZhiQianModbusClientAdapter : IAsyncDisposable {
    /// <summary>
    /// 当前连接状态。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 建立连接。
    /// </summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接。
    /// </summary>
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取 32 路 DO 线圈状态。
    /// </summary>
    /// <returns>长度固定为 32，索引 0 对应 Y01。</returns>
    ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 写单路 DO。
    /// </summary>
    /// <param name="doIndex">DO 索引（1~32）。</param>
    /// <param name="isOn">目标状态。</param>
    ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量写 DO（键：DO 索引，值：目标状态）。
    /// </summary>
    ValueTask WriteBatchDoAsync(
        IReadOnlyDictionary<int, bool> doStates,
        CancellationToken cancellationToken = default);
}
```

#### 10.20.2 读 32 路 DO 与写单路示例（TouchSocket.Modbus）

```csharp
using TouchSocket.Modbus;

/// <summary>
/// 智嵌继电器读写示例（片段）。
/// </summary>
public sealed class ZhiQianModbusReadWriteExample {
    /// <summary>
    /// DO 索引基准，Y 路从 1 开始编号。
    /// </summary>
    private const int DoIndexBase = 1;
    private readonly IModbusMaster _master;
    private readonly byte _slaveAddress;
    private readonly int _timeoutMs;

    /// <summary>
    /// 初始化示例实例。
    /// </summary>
    public ZhiQianModbusReadWriteExample(IModbusMaster master, byte slaveAddress, int timeoutMs) {
        _master = master;
        _slaveAddress = slaveAddress;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// 读取 32 路 DO（FC01，读线圈）。
    /// </summary>
    /// <remarks>
    /// 步骤：1) 从线圈起始地址读取 32 位；2) 返回 Y01~Y32 状态数组。
    /// </remarks>
    public async ValueTask<IReadOnlyList<bool>> ReadDoStatesAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var coils = await _master.ReadCoilsAsync(_slaveAddress, 0, 32, _timeoutMs, cancellationToken).ConfigureAwait(false);
        return coils;
    }

    /// <summary>
    /// 写单路 DO（FC05，写单线圈）。
    /// </summary>
    /// <remarks>
    /// 步骤：1) 校验 doIndex 范围；2) 换算线圈地址；3) 执行写入。
    /// </remarks>
    public async ValueTask WriteSingleDoAsync(int doIndex, bool isOn, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (doIndex < DoIndexBase || doIndex > 32) {
            throw new ArgumentOutOfRangeException(nameof(doIndex), "DO 索引必须在 1~32 范围。");
        }

        // Y 路从 1 开始编号，线圈地址从 0 开始编号，因此需要减去基准偏移。
        var coilAddress = (ushort)(doIndex - DoIndexBase);
        await _master.WriteSingleCoilAsync(_slaveAddress, coilAddress, isOn, _timeoutMs, cancellationToken).ConfigureAwait(false);
    }
}
```

#### 10.20.3 批量写与写后读校验示例（高性能推荐）

```csharp
using System.IO;
using Polly;
using Polly.Retry;
using TouchSocket.Modbus;

/// <summary>
/// 批量写与校验示例（片段）。
/// </summary>
public sealed class ZhiQianBatchWriteExample {
    /// <summary>
    /// DO 索引基准，Y 路从 1 开始编号。
    /// </summary>
    private const int DoIndexBase = 1;
    private readonly IModbusMaster _master;
    private readonly byte _slaveAddress;
    private readonly int _timeoutMs;
    private readonly AsyncRetryPolicy _retryPolicy;

    /// <summary>
    /// 初始化示例实例。
    /// </summary>
    public ZhiQianBatchWriteExample(IModbusMaster master, byte slaveAddress, int timeoutMs) {
        _master = master;
        _slaveAddress = slaveAddress;
        _timeoutMs = timeoutMs;
        _retryPolicy = Policy
            .Handle<TimeoutException>()
            .Or<IOException>()
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromMilliseconds(50 * attempt));
    }

    /// <summary>
    /// 批量写 DO（FC0F，写多线圈）。
    /// </summary>
    /// <remarks>
    /// 步骤：1) 构造 32 位目标数组；2) 一次性写入；3) 可选回读校验。
    /// </remarks>
    public async ValueTask WriteBatchDoWithVerifyAsync(
        IReadOnlyDictionary<int, bool> doStates,
        bool enableWriteBackVerify,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        var targetStates = (await _master
            .ReadCoilsAsync(_slaveAddress, 0, 32, _timeoutMs, cancellationToken)
            .ConfigureAwait(false))
            .ToArray();
        foreach (var pair in doStates) {
            if (pair.Key < 1 || pair.Key > 32) {
                throw new ArgumentOutOfRangeException(nameof(doStates), "批量写入存在越界 DO 索引。");
            }

            targetStates[pair.Key - DoIndexBase] = pair.Value;
        }

        await _retryPolicy.ExecuteAsync(async ct => {
            await _master.WriteMultipleCoilsAsync(_slaveAddress, 0, targetStates, _timeoutMs, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (!enableWriteBackVerify) {
            return;
        }

        var actualStates = await _master.ReadCoilsAsync(_slaveAddress, 0, 32, _timeoutMs, cancellationToken).ConfigureAwait(false);
        foreach (var pair in doStates) {
            if (actualStates[pair.Key - DoIndexBase] != pair.Value) {
                throw new InvalidOperationException($"写后读校验失败：Y{pair.Key:D2} 目标={pair.Value} 实际={actualStates[pair.Key - DoIndexBase]}");
            }
        }
    }
}
```

#### 10.20.4 `IChuteManager` 调用映射示例（从格口到 Y 路）

```csharp
/// <summary>
/// 将 IChuteManager 强排动作映射到智嵌 DO 写入（示例片段）。
/// </summary>
public async ValueTask<bool> SetForcedChuteAsync(
    long? chuteId,
    IReadOnlyDictionary<long, int> chuteToDoMap,
    IZhiQianModbusClientAdapter adapter,
    CancellationToken cancellationToken = default) {
    const int doIndexBase = 1;
    cancellationToken.ThrowIfCancellationRequested();

    if (chuteId is null) {
        return true;
    }

    if (!chuteToDoMap.TryGetValue(chuteId.Value, out var doIndex)) {
        throw new KeyNotFoundException($"未找到格口映射：chuteId={chuteId.Value}");
    }

    await adapter.WriteSingleDoAsync(doIndex, true, cancellationToken).ConfigureAwait(false);
    var states = await adapter.ReadDoStatesAsync(cancellationToken).ConfigureAwait(false);
    return states[doIndex - doIndexBase];
}
```

> 说明：以上代码为“实现模板”，目的是明确读写路径与映射方式；实际落库代码需接入统一日志与 `SafeExecutor`。

## 11. 实施顺序建议（落地清单）

1. 先做最小驱动：连接、单路开闭、批量开闭、状态回读；
2. 接入 `IChuteManager` 的 `Connect/Disconnect/SetForcedChute/SetChuteLocked`；
3. 增加延时断开（映射格口时窗）；
4. 增加 DI（若硬件支持）与状态事件；
5. 完成回归：配置软件手动验证 + 自动化协议测试。

---

## 12. 引用出处（按手册章节）

- 产品快速入门、默认参数、配置软件与网络调试助手：第 2.1~2.4 节。  
- 继电器接线与指示灯：第 4.2、4.3 节。  
- 模块参数配置（软件/网页/模式/重启生效）：第 5.1、5.2 节。  
- 心跳包/注册包、级联、透传策略：第 6.1、6.2、6.5 节。  
- 协议细节与示例（自定义/ASCII/Modbus RTU/TCP）：第 7.1~7.5 节。  
- 恢复出厂与固件升级：第 8.1、8.2 节。  
- 云接入与设备间联动案例：第 9.1、9.2 节。  
- 常见故障处理：手册“常见故障处理”章节。  
