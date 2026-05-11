# Imprivata / EVIDEN SSO Deployment — Hospital Clinical Scenario Value Analysis

## Overview

**Imprivata OneSign** and **EVIDEN Enterprise SSO** (formerly Evidian, part of Atos)
are the two leading enterprise-grade Single Sign-On (SSO) and identity management platforms
in the healthcare industry. Both offer highly similar functionality and address the same
core problem: clinical staff in hospitals need to frequently log into multiple clinical systems
(Epic, Cerner, PACS, etc.) on shared workstations every day — traditional username/password
approaches are both time-consuming and insecure.

Both platforms use proximity cards, fingerprint, PIN and other rapid authentication methods
to reduce each login from 30-60 seconds down to under 2 seconds, while ensuring every action
is traceable to an individual, meeting HIPAA/JCI compliance requirements.

**Core Positioning**: Imprivata / EVIDEN are not clinical applications (they don't replace Epic/Cerner),
nor are they directory servers (they don't replace AD). They are the **identity middleware layer**
connecting the "identity source" (AD) with the "clinical application consumers" (Epic/Cerner/PACS).

| Product | Vendor | Predecessor | Primary Market |
|---------|--------|-------------|----------------|
| **Imprivata OneSign** | Imprivata (USA) | — | North American healthcare market leader |
| **EVIDEN Enterprise SSO** | EVIDEN / Atos (Europe) | Evidian | European healthcare and enterprise market |

---

## Deployment Architecture (Component Diagram)

The following architecture is common to both Imprivata OneSign and EVIDEN Enterprise SSO — their deployment models are nearly identical:

```plantuml
@startuml SSO_Deployment

title Imprivata / EVIDEN SSO — Common Hospital Deployment Architecture

skinparam componentStyle rectangle
skinparam backgroundColor white

package "Authentication Endpoints" {
  [Proximity Card\nReader] as CardReader
  [Fingerprint\nScanner] as Fingerprint
  [PIN Pad] as PINPad
  [Username/Password\nInput] as Password
}

package "SSO Platform\n(Imprivata OneSign or EVIDEN Enterprise SSO)" #LightBlue {
  [SSO Server / Appliance\n(Authentication Engine)] as Engine
  [Credential Vault] as Vault
  [SSO Agent\n(Workstation Agent)] as Agent
  [Admin Console] as Admin
}

package "Directory Servers" {
  [MS Active\nDirectory] as AD
  [Sun ONE\nDirectory] as SunONE
  [Oracle Internet\nDirectory] as Oracle
}

package "Clinical Applications" {
  [Epic\n(EHR)] as Epic
  [Cerner\n(Clinical Info System)] as Cerner
  [PACS\n(Imaging)] as PACS
  [Pharmacy\nSystem] as Pharmacy
}

package "Workstations" {
  [Nursing Station PC\n(Shared)] as NursePC
  [Computer on Wheels\n(COW)] as COW
  [Physician Office PC] as DoctorPC
}

CardReader --> Agent
Fingerprint --> Agent
PINPad --> Agent
Password --> Agent

Agent --> Engine : Auth Request
Engine --> Vault : Query/Store Credentials
Engine --> AD : LDAP User Sync
Engine ..> SunONE : Optional
Engine ..> Oracle : Optional

Agent --> Epic : SSO Auto-Login
Agent --> Cerner : SSO Auto-Login
Agent --> PACS : SSO Auto-Login
Agent --> Pharmacy : SSO Auto-Login

NursePC --> Agent
COW --> Agent
DoctorPC --> Agent

Admin --> Engine : Configuration

note bottom of Engine
  Both sync user info from AD
  Support multiple directory servers
  ─────────────────────
  Imprivata: OneSign Appliance
  EVIDEN: Enterprise SSO Server
end note

note bottom of Agent
  SSO Agent installed on every workstation
  Intercepts clinical app login dialogs
  Auto-injects cached credentials
  ─────────────────────
  Imprivata: OneSign Agent
  EVIDEN: Enterprise SSO Agent
end note

@enduml
```

---

## SSO Authentication Sequence Diagram

The following flow applies to both Imprivata and EVIDEN (identical working mechanism):

```plantuml
@startuml SSO_Sequence

title Imprivata / EVIDEN SSO Full Flow - Tap-and-Go + Fast User Switching

actor "Clinician\n(Doctor/Nurse)" as Clinician
participant "Proximity Card\nReader" as CardReader
participant "Shared Workstation\n(SSO Agent)" as Workstation
participant "SSO Server\n(Imprivata/EVIDEN)" as Imprivata
participant "Active Directory" as AD
participant "Epic / Cerner\n(Clinical App)" as ClinicalApp

== Tap-and-Go Login ==

Clinician -> CardReader : Tap proximity card (< 1 sec)
activate CardReader

CardReader -> Workstation : Card ID
activate Workstation

Workstation -> Imprivata : Auth Request (Card ID)
activate Imprivata

note over Imprivata
  Look up user mapped to Card ID
  (User mapping synced from AD)
end note

Imprivata -> Imprivata : Validate Card ID → Match user identity

alt Second factor required (admin-configured)
  Imprivata --> Workstation : Request PIN / Fingerprint
  Workstation --> Clinician : Prompt for PIN or fingerprint
  Clinician -> Workstation : Enter PIN / Touch fingerprint
  Workstation -> Imprivata : Second factor verification
end

Imprivata --> Workstation : Auth Success + SSO Token
deactivate Imprivata

Workstation -> Workstation : Unlock Windows desktop

Workstation -> ClinicalApp : Auto-launch clinical app\n+ Inject cached credentials (SSO)
activate ClinicalApp

ClinicalApp --> Workstation : App ready
deactivate ClinicalApp

Workstation --> Clinician : Desktop + clinical apps\nall ready (total < 2 sec)
deactivate Workstation
deactivate CardReader

== Fast User Switching (Shared Workstation Scenario) ==

actor "Next Nurse" as Nurse2

Clinician -> CardReader : Leave (remove card / timeout)
activate Workstation

Workstation -> Workstation : Auto-lock screen\n(Protect patient privacy)

note over Workstation
  Previous user session securely suspended
  No manual logout required
end note

Nurse2 -> CardReader : Tap own proximity card
activate CardReader

CardReader -> Workstation : New Card ID

Workstation -> Imprivata : Authenticate new user
activate Imprivata
Imprivata --> Workstation : Auth Success
deactivate Imprivata

Workstation -> Workstation : Switch to new user desktop\n(< 3 sec)

Workstation -> ClinicalApp : Enter clinical app\nas new user
Workstation --> Nurse2 : New user ready
deactivate Workstation
deactivate CardReader

@enduml
```

---

## Use Case Diagram

```plantuml
@startuml SSO_UseCases

left to right direction
skinparam actorStyle awesome

actor "Physician" as Doctor
actor "Nurse" as Nurse
actor "IT Admin" as ITAdmin
actor "Compliance\nAuditor" as Auditor

actor "Active\nDirectory" as AD
actor "Epic" as Epic
actor "Cerner" as Cerner
actor "PACS" as PACS

rectangle "Imprivata / EVIDEN SSO" {
  usecase "Tap-and-Go\n(Proximity Card Login)" as UC_Tap
  usecase "Fingerprint Auth\n(Biometric)" as UC_Finger
  usecase "PIN Auth\n(Personal ID Number)" as UC_PIN
  usecase "Password Login\n(Username/Password)" as UC_Pwd
  usecase "Fast User Switching\n(Shared Workstation)" as UC_Switch
  usecase "HIPAA/JCI Audit\n(Complete Audit Trail)" as UC_Audit
  usecase "Directory Sync\n(AD/LDAP Integration)" as UC_DirSync
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
  Tap proximity card → Login in < 2 sec
  Replaces 30-60 sec manual input
end note

note bottom of UC_Audit
  Each authentication records:
  Who / When
  Which workstation (Where)
  Which app accessed (What)
end note

note bottom of UC_Switch
  Nursing station / ER shared PCs
  No logout needed → Tap to switch
  Previous user session protected
end note

@enduml
```

---

## Directory Server Integration

Both admin consoles support connecting to multiple enterprise directory servers for user identity synchronization:

| Directory Server Type | Imprivata | EVIDEN | Description |
|-----------------------|:---------:|:------:|-------------|
| **MS Active Directory** | ✓ | ✓ | Most common, standard hospital IT infrastructure |
| **NT Domain** | ✓ | ✓ | Legacy Windows domain, backward compatibility |
| **NetWare NDS/eDirectory** | ✓ | ✓ | Novell directory service |
| **Sun ONE Directory Server** | ✓ | ✓ | Sun/Oracle LDAP directory |
| **Oracle Internet Directory** | ✓ | ✓ | Oracle enterprise directory service |
| **Standard LDAP v3** | ✓ | ✓ | Any LDAPv3-compliant directory |

**Synchronization Mechanism** (similar for both):
- Sync user identity information from directory servers
- Proximity card IDs / fingerprint templates stored in local credential vault
- User disabled in AD → SSO platform auto-syncs, proximity card immediately invalidated

> Reference: Imprivata OneSign Admin Console "Select Directory Server" interface
> Supports wizard-based directory type selection → Connection parameters → Sync rules → Preview users

---

## Why Do Hospitals With Epic/Cerner Still Need Imprivata / EVIDEN?

```plantuml
@startuml Why_SSO

title The Identity Gap: Why Epic/Cerner Cannot Solve the Login Problem

skinparam backgroundColor white
skinparam componentStyle rectangle

package "Identity Source (Who is who?)" {
  [Active Directory\n(Users/Passwords/Groups)] as AD
}

package "Clinical Applications (What to do?)" {
  [Epic] as Epic
  [Cerner] as Cerner
  [PACS] as PACS
  [Pharmacy] as Pharm
  [...10+ other apps] as Others
}

note as Gap
  <b>The Identity Gap</b>
  Each app independently requires login
  Nurses enter passwords 60-70 times/shift
  ──────────────────
  Epic/Cerner are clinical systems
  They are <b>NOT</b> identity platforms
  They cannot help you log into other apps
end note

AD -[hidden]-> Gap
Gap -[hidden]-> Epic

package "Imprivata / EVIDEN\nBridges the Gap" #LightGreen {
  [SSO Platform\n(Identity Middleware)] as IMP
}

AD --> IMP : User Sync
IMP --> Epic : SSO
IMP --> Cerner : SSO
IMP --> PACS : SSO
IMP --> Pharm : SSO
IMP --> Others : SSO

note bottom of IMP
  One authentication (Tap/Fingerprint/PIN)
  → All clinical apps automatically available
  → Complete audit trail
end note

@enduml
```

**Key Insights**:
- **Epic/Cerner** handle clinical workflows (orders, records, labs) — they are information **consumers**
- **Active Directory** handles "who is this person" — it is the identity **source**
- **Imprivata / EVIDEN** handle "how to quickly and securely prove who you are" — they are the identity **bridge**

Without an SSO platform, each app asks "who are you?" independently → nurses repeatedly enter passwords every shift.
With Imprivata / EVIDEN, prove identity once → all apps automatically trust you.

---

## Imprivata vs EVIDEN Comparison

| Dimension | Imprivata OneSign | EVIDEN Enterprise SSO |
|-----------|-------------------|----------------------|
| **Vendor / HQ** | Imprivata Inc. (Massachusetts, USA) | EVIDEN (Atos Group, France) |
| **Predecessor** | — | Evidian (renamed 2023 with Atos restructuring) |
| **Primary Market** | North American healthcare (market leader) | European healthcare and enterprise |
| **Proximity Card Auth** | ✓ (Tap-and-Go) | ✓ (Badge SSO) |
| **Fingerprint Auth** | ✓ | ✓ |
| **PIN Auth** | ✓ | ✓ |
| **Password Auth** | ✓ | ✓ |
| **RFID/NFC Cards** | ✓ | ✓ |
| **Fast User Switching** | ✓ (Tap-out/Tap-in) | ✓ (Fast User Switching) |
| **SSO Agent** | OneSign Desktop Agent | Enterprise SSO Agent |
| **AD/LDAP Integration** | ✓ | ✓ |
| **Audit Logging** | ✓ | ✓ |
| **Deployment Model** | Physical/Virtual Appliance | Software Server |
| **Epic Integration** | Deep integration (Epic certified partner) | Via SSO Profile |
| **Cerner Integration** | ✓ | ✓ |
| **Citrix/VDI** | ✓ (Virtual Desktop Access) | ✓ (VDI SSO) |
| **Compliance Certs** | HIPAA, EPCS, DEA | HIPAA, GDPR, HDS |
| **Admin Console** | Web-based OneSign Admin | Web-based Admin Console |

**Selection Guidance**:
- North American hospitals, need deep Epic integration → **Imprivata**
- European hospitals, existing Atos/EVIDEN ecosystem → **EVIDEN**
- Functionally nearly equivalent — choice depends more on regional support and existing IT ecosystem

---

## Key Design Points

| Layer | Function | Description |
|-------|----------|-------------|
| Hardware | Proximity Card / Fingerprint Scanner / PIN Pad | Physical authentication factors, replacing keyboard password entry |
| SSO Platform ↔ AD | LDAP/LDAPS User Sync | Sync user information, supports multiple directory servers |
| SSO Platform ↔ Workstation | SSO Agent (Workstation Agent) | Intercepts app login dialogs, injects cached credentials |
| SSO Platform ↔ Clinical Apps | SSO Profile (Login Template) | Per-application auto-login configuration |
| Audit Layer | Operation Logs | Records Who/When/Where/What for every authentication |
