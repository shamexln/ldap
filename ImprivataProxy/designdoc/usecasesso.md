# Imprivata / EVIDEN SSO 部署 — 医院临床场景价值分析

## 概述

**Imprivata OneSign** 和 **EVIDEN Enterprise SSO**（前身为 Evidian，原属 Atos）
是医疗行业两大主流企业级单点登录(SSO)和身份管理平台。两者功能高度相似，
解决的核心问题相同：医院内临床人员每天需要在共享工作站上频繁登录多个临床系统
（Epic、Cerner、PACS 等），传统用户名/密码方式既耗时又不安全。

两者都通过感应卡(Proximity Card)、指纹、PIN 等快速认证方式，将每次登录
从 30-60 秒缩短到 2 秒以内，同时确保每次操作都可追溯到个人，满足 HIPAA/JCI 合规要求。

**核心定位**: Imprivata / EVIDEN 不是临床应用（不替代 Epic/Cerner），也不是目录服务器（不替代 AD）。
它们是连接"身份源"（AD）与"临床应用消费者"（Epic/Cerner/PACS）之间的**身份中间层**。

| 产品 | 厂商 | 前身 | 主要市场 |
|------|------|------|----------|
| **Imprivata OneSign** | Imprivata (美国) | — | 北美医疗市场领导者 |
| **EVIDEN Enterprise SSO** | EVIDEN / Atos (欧洲) | Evidian | 欧洲医疗及企业市场 |

---

## 部署架构图 (Component Diagram)

以下架构对 Imprivata OneSign 和 EVIDEN Enterprise SSO 通用——两者部署模式几乎相同：

```plantuml
@startuml SSO_Deployment

title Imprivata / EVIDEN SSO 医院通用部署架构

skinparam componentStyle rectangle
skinparam backgroundColor white

package "认证终端 (Authentication Endpoints)" {
  [接近感应卡\n读卡器] as CardReader
  [指纹扫描器] as Fingerprint
  [PIN 键盘] as PINPad
  [用户名/密码\n输入] as Password
}

package "SSO 平台\n(Imprivata OneSign 或 EVIDEN Enterprise SSO)" #LightBlue {
  [SSO Server / Appliance\n(认证引擎)] as Engine
  [凭证保险库\n(Credential Vault)] as Vault
  [SSO Agent\n(工作站代理)] as Agent
  [管理控制台\n(Admin Console)] as Admin
}

package "目录服务器 (Directory Servers)" {
  [MS Active\nDirectory] as AD
  [Sun ONE\nDirectory] as SunONE
  [Oracle Internet\nDirectory] as Oracle
}

package "临床应用 (Clinical Applications)" {
  [Epic\n(电子病历)] as Epic
  [Cerner\n(临床信息系统)] as Cerner
  [PACS\n(影像系统)] as PACS
  [药房系统\n(Pharmacy)] as Pharmacy
}

package "工作站 (Workstations)" {
  [护士站 PC\n(共享)] as NursePC
  [移动推车\n(COW)] as COW
  [医生办公室 PC] as DoctorPC
}

CardReader --> Agent
Fingerprint --> Agent
PINPad --> Agent
Password --> Agent

Agent --> Engine : 认证请求
Engine --> Vault : 查询/存储凭证
Engine --> AD : LDAP 用户同步
Engine ..> SunONE : 可选
Engine ..> Oracle : 可选

Agent --> Epic : SSO 自动登录
Agent --> Cerner : SSO 自动登录
Agent --> PACS : SSO 自动登录
Agent --> Pharmacy : SSO 自动登录

NursePC --> Agent
COW --> Agent
DoctorPC --> Agent

Admin --> Engine : 配置管理

note bottom of Engine
  两者都从 AD 同步用户信息
  支持多种目录服务器
  ─────────────────────
  Imprivata: OneSign Appliance
  EVIDEN: Enterprise SSO Server
end note

note bottom of Agent
  SSO Agent 安装在每台工作站
  拦截临床应用登录界面
  自动注入已缓存的凭证
  ─────────────────────
  Imprivata: OneSign Agent
  EVIDEN: Enterprise SSO Agent
end note

@enduml
```

---

## SSO 认证时序图 (Sequence Diagram)

以下流程对 Imprivata 和 EVIDEN 通用（两者工作机制相同）：

```plantuml
@startuml SSO_Sequence

title Imprivata / EVIDEN SSO 完整流程 - Tap-and-Go + 快速用户切换

actor "临床医生/护士" as Clinician
participant "接近感应卡\n读卡器" as CardReader
participant "共享工作站\n(SSO Agent)" as Workstation
participant "SSO Server\n(Imprivata/EVIDEN)" as Imprivata
participant "Active Directory" as AD
participant "Epic / Cerner\n(临床应用)" as ClinicalApp

== Tap-and-Go 登录 ==

Clinician -> CardReader : 轻触感应卡 (< 1秒)
activate CardReader

CardReader -> Workstation : 卡号 (Card ID)
activate Workstation

Workstation -> Imprivata : 认证请求 (Card ID)
activate Imprivata

note over Imprivata
  查找卡号对应的用户
  (已从 AD 同步的用户映射)
end note

Imprivata -> Imprivata : 验证卡号 → 匹配用户身份

alt 需要第二因素 (管理员配置)
  Imprivata --> Workstation : 请求 PIN / 指纹
  Workstation --> Clinician : 提示输入 PIN 或触摸指纹
  Clinician -> Workstation : 输入 PIN / 按指纹
  Workstation -> Imprivata : 第二因素验证
end

Imprivata --> Workstation : 认证成功 + SSO Token
deactivate Imprivata

Workstation -> Workstation : 解锁 Windows 桌面

Workstation -> ClinicalApp : 自动启动临床应用\n+ 注入缓存凭证 (SSO)
activate ClinicalApp

ClinicalApp --> Workstation : 应用就绪
deactivate ClinicalApp

Workstation --> Clinician : 桌面 + 临床应用\n全部就绪 (总耗时 < 2秒)
deactivate Workstation
deactivate CardReader

== 快速用户切换 (共享工作站场景) ==

actor "下一位护士" as Nurse2

Clinician -> CardReader : 离开 (拔卡 / 超时)
activate Workstation

Workstation -> Workstation : 自动锁屏\n(保护患者隐私)

note over Workstation
  前一用户会话被安全挂起
  无需手动注销
end note

Nurse2 -> CardReader : 轻触自己的感应卡
activate CardReader

CardReader -> Workstation : 新卡号

Workstation -> Imprivata : 认证新用户
activate Imprivata
Imprivata --> Workstation : 认证成功
deactivate Imprivata

Workstation -> Workstation : 切换到新用户桌面\n(< 3秒完成)

Workstation -> ClinicalApp : 以新用户身份\n进入临床应用
Workstation --> Nurse2 : 新用户就绪
deactivate Workstation
deactivate CardReader

@enduml
```

---

## 用例图 (Use Case Diagram)

```plantuml
@startuml SSO_UseCases

left to right direction
skinparam actorStyle awesome

actor "临床医生" as Doctor
actor "护士" as Nurse
actor "IT 管理员" as ITAdmin
actor "合规审计员" as Auditor

actor "Active\nDirectory" as AD
actor "Epic" as Epic
actor "Cerner" as Cerner
actor "PACS" as PACS

rectangle "Imprivata / EVIDEN SSO" {
  usecase "Tap-and-Go\n(感应卡即时登录)" as UC_Tap
  usecase "指纹认证\n(生物识别)" as UC_Finger
  usecase "PIN 认证\n(个人识别码)" as UC_PIN
  usecase "密码登录\n(用户名/密码)" as UC_Pwd
  usecase "快速用户切换\n(共享工作站)" as UC_Switch
  usecase "HIPAA/JCI 审计\n(完整操作日志)" as UC_Audit
  usecase "目录服务器同步\n(AD/LDAP 集成)" as UC_DirSync
}

Doctor --> UC_Tap
Doctor --> UC_Finger
Doctor --> UC_PIN
Doctor --> UC_Pwd

Nurse --> UC_Tap
Nurse --> UC_Finger
Nurse --> UC_Switch

ITAdmin --> UC_DirSync
ITAdmin --> UC_Pwd

Auditor --> UC_Audit

UC_Tap --> Epic
UC_Tap --> Cerner
UC_Finger --> Epic
UC_Finger --> PACS
UC_PIN --> Cerner
UC_Pwd --> Epic
UC_Pwd --> Cerner

UC_DirSync --> AD

note bottom of UC_Tap
  轻触感应卡 → 2秒内完成登录
  替代 30-60秒的手动输入
end note

note bottom of UC_Audit
  每次认证记录:
  谁(Who) / 何时(When)
  哪台工作站(Where)
  访问了什么应用(What)
end note

note bottom of UC_Switch
  护士站 / 急诊共享 PC
  无需注销 → 拍卡即切换
  保护前一用户会话
end note

@enduml
```

---

## 目录服务器集成

两者的管理后台 (Admin Console) 都支持连接多种企业目录服务器，用于同步用户身份信息：

| 目录服务器类型 | Imprivata | EVIDEN | 说明 |
|---------------|:---------:|:------:|------|
| **MS Active Directory** | ✓ | ✓ | 最常用，医院 IT 基础设施标配 |
| **NT Domain** | ✓ | ✓ | 传统 Windows 域，兼容旧系统 |
| **NetWare NDS/eDirectory** | ✓ | ✓ | Novell 目录服务 |
| **Sun ONE Directory Server** | ✓ | ✓ | Sun/Oracle LDAP 目录 |
| **Oracle Internet Directory** | ✓ | ✓ | Oracle 企业目录服务 |
| **标准 LDAP v3** | ✓ | ✓ | 任何兼容 LDAPv3 的目录 |

**同步机制** (两者类似):
- 从目录服务器同步用户身份信息
- 感应卡号 / 指纹模板存储在本地凭证保险库
- 用户在 AD 中被禁用 → SSO 平台自动同步，感应卡立即失效

> 参考: Imprivata OneSign 管理后台 "Select Directory Server" 界面
> 支持在向导中选择目录类型 → 配置连接参数 → 设置同步规则 → 预览用户

---

## 为什么医院有 Epic/Cerner 还需要 Imprivata / EVIDEN？

```plantuml
@startuml Why_SSO

title 身份鸿沟: 为什么 Epic/Cerner 不能解决登录问题

skinparam backgroundColor white
skinparam componentStyle rectangle

package "身份源 (谁是谁?)" {
  [Active Directory\n(用户/密码/组)] as AD
}

package "临床应用 (做什么?)" {
  [Epic] as Epic
  [Cerner] as Cerner
  [PACS] as PACS
  [药房] as Pharm
  [...其他 10+ 应用] as Others
}

note as Gap
  <b>身份鸿沟</b>
  每个应用独立要求登录
  护士每班输入 60-70 次密码
  ──────────────────
  Epic/Cerner 是临床系统
  <b>不是</b>身份管理平台
  它们无法帮你登录其他应用
end note

AD -[hidden]-> Gap
Gap -[hidden]-> Epic

package "Imprivata / EVIDEN\n填补鸿沟" #LightGreen {
  [SSO 平台\n(身份中间层)] as IMP
}

AD --> IMP : 用户同步
IMP --> Epic : SSO
IMP --> Cerner : SSO
IMP --> PACS : SSO
IMP --> Pharm : SSO
IMP --> Others : SSO

note bottom of IMP
  一次认证 (Tap/指纹/PIN)
  → 所有临床应用自动可用
  → 完整审计链
end note

@enduml
```

**关键洞察**:
- **Epic/Cerner** 负责临床工作流（医嘱、病历、检验）— 它们是信息的**消费者**
- **Active Directory** 负责"这个人是谁"— 它是身份的**源头**
- **Imprivata / EVIDEN** 负责"如何快速、安全地证明你是谁"— 它们是身份的**桥梁**

没有 SSO 平台，每个应用各自问一遍"你是谁？"→ 护士每班反复输入密码。
有了 Imprivata / EVIDEN，证明一次身份 → 所有应用自动信任。

---

## Imprivata vs EVIDEN 对比

| 对比维度 | Imprivata OneSign | EVIDEN Enterprise SSO |
|----------|-------------------|----------------------|
| **厂商/总部** | Imprivata Inc. (美国马萨诸塞) | EVIDEN (Atos 集团, 法国) |
| **前身** | — | Evidian (2023年随 Atos 重组更名) |
| **主要市场** | 北美医疗 (市占率领先) | 欧洲医疗及企业 |
| **感应卡认证** | ✓ (Tap-and-Go) | ✓ (Badge SSO) |
| **指纹认证** | ✓ | ✓ |
| **PIN 认证** | ✓ | ✓ |
| **密码认证** | ✓ | ✓ |
| **RFID/NFC 卡** | ✓ | ✓ |
| **快速用户切换** | ✓ (Tap-out/Tap-in) | ✓ (Fast User Switching) |
| **SSO Agent** | OneSign Desktop Agent | Enterprise SSO Agent |
| **AD/LDAP 集成** | ✓ | ✓ |
| **审计日志** | ✓ | ✓ |
| **部署方式** | 物理/虚拟 Appliance | 软件服务器 |
| **Epic 集成** | 深度集成 (Epic 认证合作伙伴) | 通过 SSO Profile 支持 |
| **Cerner 集成** | ✓ | ✓ |
| **Citrix/VDI** | ✓ (Virtual Desktop Access) | ✓ (VDI SSO) |
| **合规认证** | HIPAA, EPCS, DEA | HIPAA, GDPR, HDS |
| **管理控制台** | Web-based OneSign Admin | Web-based Admin Console |

**选择建议**:
- 北美医院、需要 Epic 深度集成 → **Imprivata**
- 欧洲医院、已有 Atos/EVIDEN 生态 → **EVIDEN**
- 功能上两者几乎等价，选择更多取决于区域支持和现有 IT 生态

---

## 关键设计点

| 层级 | 功能 | 说明 |
|------|------|------|
| 硬件层 | 接近感应卡 / 指纹扫描器 / PIN 键盘 | 物理认证因子，替代键盘输入密码 |
| SSO 平台 ↔ AD | LDAP/LDAPS 用户同步 | 同步用户信息，支持多种目录服务器 |
| SSO 平台 ↔ 工作站 | SSO Agent (工作站代理) | 拦截应用登录对话框，注入缓存凭证 |
| SSO 平台 ↔ 临床应用 | SSO Profile (登录模板) | 每个应用一套自动登录配置文件 |
| 审计层 | 操作日志 | 记录每次认证的 Who/When/Where/What |
