# ImprivataProxy Use Case

## 概述

监护仪通过 Gateway 发送 Imprivata 协议的认证请求，ImprivataProxy 伪装成 Imprivata 服务器接收请求，
将其转换为 LDAP(AD) 请求，再将 LDAP(AD) 响应翻译回 Imprivata 格式返回给 Gateway。

Gateway 完全不知道后面不是真正的 Imprivata 服务器。

---

## 时序图 (Sequence Diagram)

```plantuml
@startuml ImprivataProxy_Sequence

title ImprivataProxy 认证流程 - 伪装 Imprivata 转发至 LDAP(AD)

actor "临床用户" as User
participant "监护仪\n(Patient Monitor)" as Monitor
participant "Gateway" as Gateway
participant "ImprivataProxy\n(我们的代码)" as Proxy
participant "LDAP (AD)\nServer" as LDAP

== 用户登录 / 刷卡 ==

User -> Monitor : 点击登录 / 刷卡
activate Monitor

Monitor -> Gateway : 登录请求\n(用户名/密码 或 卡号)
activate Gateway

Gateway -> Proxy : Imprivata XML 请求\n(AuthenticateUser / GetUserIdentity)
activate Proxy

note over Proxy
  解析 Imprivata XML 请求
  提取用户凭证信息
end note

Proxy -> LDAP : LDAP Bind + Search\n(协议转换: Imprivata → LDAP)
activate LDAP

LDAP --> Proxy : LDAP 响应\n(认证结果 + 用户属性)
deactivate LDAP

note over Proxy
  将 LDAP 响应翻译为
  Imprivata XML 格式
end note

Proxy --> Gateway : Imprivata XML 响应\n(伪装的 Imprivata 应答)
deactivate Proxy

Gateway --> Monitor : 登录结果 (成功/失败)
deactivate Gateway

Monitor --> User : 显示登录状态
deactivate Monitor

== 获取域列表 ==

Monitor -> Gateway : 请求域列表
activate Gateway

Gateway -> Proxy : Imprivata GetDomains 请求
activate Proxy

Proxy -> LDAP : 查询可用域
activate LDAP

LDAP --> Proxy : 域列表
deactivate LDAP

Proxy --> Gateway : Imprivata XML 域列表响应
deactivate Proxy

Gateway --> Monitor : 域列表
deactivate Gateway

@enduml
```

---

## 用例图 (Use Case Diagram)

```plantuml
@startuml ImprivataProxy_UseCase

left to right direction
skinparam actorStyle awesome

actor "临床用户" as User
actor "监护仪" as Monitor
actor "Gateway" as GW
actor "LDAP(AD)\nServer" as LDAP

rectangle "ImprivataProxy (我们的代码)" {
  usecase "接收 Imprivata\nXML 请求" as UC1
  usecase "翻译请求\nImprivata → LDAP" as UC2
  usecase "执行 LDAP\n认证/查询" as UC3
  usecase "翻译响应\nLDAP → Imprivata" as UC4
  usecase "返回 Imprivata\nXML 响应" as UC5
}

User --> Monitor : 登录/刷卡
Monitor --> GW : 发送认证请求
GW --> UC1 : Imprivata 协议\n(Gateway 认为\n对面是真 Imprivata)
UC1 --> UC2
UC2 --> UC3
UC3 --> LDAP : LDAP 协议
UC3 --> UC4
UC4 --> UC5
UC5 --> GW : Imprivata 协议

note bottom of UC1
  Gateway 完全不知道
  后面不是真正的 Imprivata
end note

@enduml
```

---

## 关键设计点

| 层级 | 协议 | 说明 |
|------|------|------|
| 监护仪 ↔ Gateway | 内部协议 | 监护仪原有逻辑不变 |
| Gateway ↔ ImprivataProxy | Imprivata XML API | Proxy 完全模拟 Imprivata 接口 |
| ImprivataProxy ↔ LDAP(AD) | LDAP 协议 | 标准 LDAP Bind/Search 操作 |
