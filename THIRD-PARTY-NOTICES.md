# Hinweise zu Drittanbieter-Software / Third-Party Notices

**Deutsch:** SKZ Connect (Windows-App, Android-App, Linux-Dienst `nconnect` und Kodi-Add-on `service.nconnect`) enthält
oder nutzt Software, Quellcode und Daten Dritter. Diese Datei nennt jede Komponente mit Version, Plattform,
Projekt-URL, Lizenz und Copyright-Vermerk und enthält die vollständigen Lizenztexte (Abschnitt D). Die Lizenz von
SKZ Connect selbst (MIT) steht in `LICENSE`. Die Rechte an den Drittkomponenten verbleiben bei ihren Inhabern.

**English:** SKZ Connect (Windows app, Android app, Linux daemon `nconnect` and Kodi add-on `service.nconnect`) includes
or uses third-party software, source code and data. This file lists every component with its version, platform,
project URL, licence and copyright notice, and contains the complete licence texts (section D). SKZ Connect's own licence
(MIT) is in `LICENSE`. All third-party components remain the property of their respective owners.

Stand / As of: 2026-10-17 – SKZ Connect für Windows 1.0.22, Android 0.9.1, Kodi-Add-on 0.4.4 (Linux-Dienst `nconnect`).

Nintendo, Nintendo Switch, Joy-Con, Wii, Xbox, PlayStation, DualShock und DualSense sind Marken ihrer jeweiligen
Inhaber. SKZ Connect ist ein unabhängiges Projekt und steht in keiner Verbindung zu diesen Unternehmen.

## Inhalt

1. [Übersicht](#1-übersicht)
2. [A – Mitgelieferte Software](#a--mitgelieferte-software)
3. [B – Übernommener oder angepasster Quellcode und Daten](#b--übernommener-oder-angepasster-quellcode-und-daten)
4. [C – Protokoll-Dokumentation (kein übernommener Code)](#c--protokoll-dokumentation-kein-übernommener-code)
5. [D – Lizenztexte](#d--lizenztexte)

## 1. Übersicht

„Alle“ = Windows, Android und Linux/Kodi (Bestandteil der gemeinsamen Protokoll-Bibliothek `Switch2Pro.Protocol`).

| Komponente | Version | Plattform | Lizenz | Abschnitt |
|---|---|---|---|---|
| Nefarius.ViGEm.Client | 1.21.256 | Windows | MIT | A.1 |
| ViGEmClient (native, in Nefarius.ViGEm.Client eingebettet) | aus 1.21.256 | Windows | MIT | A.1 |
| Costura/Fody (Ladecode in Nefarius.ViGEm.Client) | aus 1.21.256 | Windows | MIT | A.1 |
| ViGEmBus (Treiber-Installer, unverändert) | 1.22.0 | Windows-Setup | BSD-3-Clause | A.2 |
| HidHide (Treiber-Installer, unverändert) | 1.5.230.0 | Windows-Setup | MIT | A.3 |
| .NET-Laufzeit und Windows Desktop Runtime (Windows Forms) | 10.0.x | Windows | MIT | A.4 |
| C#/WinRT-Laufzeit (`WinRT.Runtime.dll`) | 2.2.0 | Windows | MIT | A.5 |
| Windows-SDK-Projektion (`Microsoft.Windows.SDK.NET.dll`) | 10.0.22621.55 | Windows | Microsoft Windows SDK License Terms | A.5 |
| Inno Setup (Setup-Programm) | 6.x | Windows-Setup | Inno Setup License | A.6 |
| .NET für Android und .NET-Laufzeit (Mono) | 36.1 / 10.0 | Android | MIT | A.7 |
| .NET-for-Android-Bindungen (`Xamarin.*`-Pakete) | siehe A.9–A.12 | Android | MIT | A.8 |
| AndroidX (58 Artefakte) | siehe A.9 | Android | Apache-2.0 | A.9 |
| Material Components for Android | 1.14.0 | Android | Apache-2.0 | A.10 |
| Kotlin-Standardbibliothek | 2.4.0 | Android | Apache-2.0 (Teile BSD-3-Clause, BSL-1.0) | A.11 |
| kotlinx.coroutines | 1.11.0 | Android | Apache-2.0 | A.11 |
| kotlinx.serialization | 1.11.0 | Android | Apache-2.0 | A.11 |
| kotlinx-atomicfu | 0.33.0 | Android | Apache-2.0 | A.11 |
| Guava ListenableFuture | 1.0 | Android | Apache-2.0 | A.12 |
| Error Prone Annotations | 2.50.0 | Android | Apache-2.0 | A.12 |
| JetBrains Java Annotations | 26.1.0 | Android | Apache-2.0 | A.12 |
| JSpecify | 1.0.0 | Android | Apache-2.0 | A.12 |
| Shizuku API (`api`, `provider`, `shared`, `aidl`) | 13.1.5 | Android | MIT | A.13 |
| Material Icons (8 Symbole) | – | Android | Apache-2.0 | A.14 |
| Linux.Bluetooth | 6.0.0-pre3 | Linux/Kodi | Apache-2.0 | A.15 |
| Tmds.DBus | 0.95.1 | Linux/Kodi | MIT | A.16 |
| .NET-Laufzeit (self-contained) | 10.0.x | Linux/Kodi | MIT | A.17 |
| SDL – Simple DirectMedia Layer (Quellcode, angepasst) | SDL 3, Stand 2026 | alle | zlib | B.1 |
| NS2Pro-Bridge-Windows (Protokollkonstanten) | Stand 2026 | alle | MIT | B.2 |
| Switch2BTLink (über NS2Pro-Bridge-Windows) | Stand 2026 | alle | MIT | B.2 |
| joycon2cpp (über NS2Pro-Bridge-Windows) | Stand 2026 | alle | MIT | B.2 |
| Android CTS – HID-Deskriptor Xbox One S (Daten) | Stand 2026 | alle (genutzt unter Android) | Apache-2.0 | B.3 |

---

## A – Mitgelieferte Software

### A.1 Nefarius.ViGEm.Client

- Version: 1.21.256 (NuGet), in `SKZ-Connect.exe` eingebunden
- Plattform: Windows
- Projekt: https://github.com/nefarius/ViGEm.NET
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright (c) 2018 Benjamin Höglinger-Stelzer
- Copyright © Nefarius Software Solutions e.U. 2017-2023 (Angabe im Paket)

In der Bibliothek eingebettet und mit ihr weitergegeben:

- **ViGEmClient** (native `ViGEmClient.dll`, x86/x64) – https://github.com/nefarius/ViGEmClient – MIT –
  Copyright (c) 2018 Benjamin Höglinger-Stelzer
- **Costura** (Fody-Add-in; Ladecode für eingebettete Bibliotheken) – https://github.com/Fody/Costura – MIT –
  Copyright (c) 2012 Simon Cropp and contributors

### A.2 ViGEmBus

- Version: 1.22.0 – offizieller, signierter Installer, unverändert weitergegeben; das SKZ-Connect-Setup installiert ihn,
  wenn kein ViGEmBus ab 1.17 vorhanden ist
- Plattform: Windows (Setup)
- Projekt: https://github.com/nefarius/ViGEmBus
- Lizenz: BSD-3-Clause (vollständiger Text: [D.4](#d4-bsd-3-clause--vigembus))
- Copyright (c) 2016-2020, Nefarius Software Solutions e.U.

### A.3 HidHide

- Version: 1.5.230.0 (x64) – offizieller, signierter Installer, unverändert weitergegeben. SKZ Connect kann außerdem
  auf Nachfrage neuere offizielle HidHide-Installer direkt von GitHub laden; für diese gilt die Lizenz der jeweiligen
  Version.
- Plattform: Windows (Setup)
- Projekt: https://github.com/nefarius/HidHide
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright (c) 2020 Eric Korff de Gidts
- Copyright (c) 2021-2023 Benjamin Höglinger-Stelzer

### A.4 .NET-Laufzeit und Windows Desktop Runtime (Windows)

- Version: .NET 10 (Microsoft.NETCore.App und Microsoft.WindowsDesktop.App 10.0.x; der Release-Build ist
  self-contained, d. h. die Laufzeit steckt in `SKZ-Connect.exe`)
- Plattform: Windows
- Projekte: https://github.com/dotnet/runtime, https://github.com/dotnet/winforms
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright (c) .NET Foundation and Contributors
- Die .NET-Laufzeit enthält ihrerseits Komponenten Dritter (z. B. zlib-ng, Brotli). Deren Hinweise stehen in
  `THIRD-PARTY-NOTICES.TXT` der Laufzeitpakete bzw. unter
  https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT und
  https://github.com/dotnet/winforms/blob/main/THIRD-PARTY-NOTICES.TXT.

### A.5 C#/WinRT und Windows-SDK-Projektion

- **C#/WinRT-Laufzeit** (`WinRT.Runtime.dll` 2.2.0) – https://github.com/microsoft/CsWinRT – MIT
  (Text: [D.1](#d1-mit-lizenz)) – Copyright (c) Microsoft Corporation.
- **Windows-SDK-Projektion** (`Microsoft.Windows.SDK.NET.dll` 10.0.22621.55, aus dem NuGet-Paket
  Microsoft.Windows.SDK.NET.Ref 10.0.22621.57) – Copyright (c) Microsoft Corporation. All rights reserved. –
  Lizenz: Microsoft Software License Terms für das Windows SDK, https://aka.ms/WinSDKLicenseURL
- Plattform: Windows

### A.6 Inno Setup

- Verwendung: Das Setup-Programm `SKZ-Connect-Setup-<Version>.exe` wird mit Inno Setup 6 erstellt und enthält dessen
  Setup-Kern sowie die Sprachdateien von Inno Setup.
- Plattform: Windows (Setup)
- Projekt: https://jrsoftware.org/isinfo.php
- Lizenz: Inno Setup License (vollständiger Text: [D.8](#d8-inno-setup-license))
- Copyright (C) 1997-2026 Jordan Russell. All rights reserved.
- Portions Copyright (C) 2000-2026 Martijn Laan. All rights reserved.

### A.7 .NET für Android und .NET-Laufzeit (Android)

- Version: .NET für Android 36.1 (Workload Microsoft.Android.Sdk) mit der .NET-10-Laufzeit (Mono) und den
  .NET-Basisbibliotheken, im APK enthalten (teilweise AOT-kompiliert)
- Plattform: Android
- Projekte: https://github.com/dotnet/android, https://github.com/dotnet/runtime
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright (c) .NET Foundation and Contributors
- Hinweise zu enthaltenen Komponenten Dritter: https://github.com/dotnet/android/blob/main/THIRD-PARTY-NOTICES.TXT und
  https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

### A.8 .NET-for-Android-Bindungen (Microsoft)

Die Java-/Kotlin-Bibliotheken in A.9–A.12 werden über NuGet-Pakete von Microsoft eingebunden (`Xamarin.AndroidX.*`,
`Xamarin.Google.*`, `Xamarin.Kotlin.*`, `Xamarin.KotlinX.*`, `Xamarin.Jetbrains.Annotations`, `Xamarin.JSpecify`).
Jedes Paket besteht aus einer C#-Bindung von Microsoft und der unveränderten Java-Bibliothek (AAR/JAR).

- Plattform: Android
- Projekt: https://github.com/dotnet/android-libraries
- Lizenz der Bindungen: MIT (Text: [D.1](#d1-mit-lizenz)); Lizenz der Java-Bibliotheken: siehe A.9–A.12
- Copyright (c) .NET Foundation Contributors (laut `LICENSE.md` der Pakete); Paketangabe:
  © Microsoft Corporation. All rights reserved.

### A.9 AndroidX

- Plattform: Android
- Projekte: https://developer.android.com/jetpack/androidx – Quellcode https://github.com/androidx/androidx
  (ConstraintLayout: https://github.com/androidx/constraintlayout)
- Lizenz: Apache-2.0 (Text: [D.2](#d2-apache-license-20)); die Artefakte enthalten keine NOTICE-Dateien
- Copyright (C) The Android Open Source Project

| Maven-Artefakt | Version | NuGet-Paket |
|---|---|---|
| `androidx.activity:activity` | 1.13.0 | Xamarin.AndroidX.Activity 1.13.0.1 |
| `androidx.annotation:annotation` | 1.10.0 | Xamarin.AndroidX.Annotation 1.10.0.1 |
| `androidx.annotation:annotation-experimental` | 1.6.0 | Xamarin.AndroidX.Annotation.Experimental 1.6.0.1 |
| `androidx.annotation:annotation-jvm` | 1.10.0 | Xamarin.AndroidX.Annotation.Jvm 1.10.0.1 |
| `androidx.appcompat:appcompat` | 1.7.1 | Xamarin.AndroidX.AppCompat 1.7.1.4 |
| `androidx.appcompat:appcompat-resources` | 1.7.1 | Xamarin.AndroidX.AppCompat.AppCompatResources 1.7.1.4 |
| `androidx.arch.core:core-common` | 2.2.0 | Xamarin.AndroidX.Arch.Core.Common 2.2.0.21 |
| `androidx.arch.core:core-runtime` | 2.2.0 | Xamarin.AndroidX.Arch.Core.Runtime 2.2.0.21 |
| `androidx.cardview:cardview` | 1.0.0 | Xamarin.AndroidX.CardView 1.0.0.39 |
| `androidx.collection:collection` | 1.6.0 | Xamarin.AndroidX.Collection 1.6.0.1 |
| `androidx.collection:collection-jvm` | 1.6.0 | Xamarin.AndroidX.Collection.Jvm 1.6.0.1 |
| `androidx.compose.runtime:runtime-annotation` | 1.11.3 | Xamarin.AndroidX.Compose.Runtime.Annotation 1.11.3.1 |
| `androidx.compose.runtime:runtime-annotation-android` | 1.11.3 | Xamarin.AndroidX.Compose.Runtime.Annotation.Android 1.11.3.1 |
| `androidx.concurrent:concurrent-futures` | 1.3.0 | Xamarin.AndroidX.Concurrent.Futures 1.3.0.4 |
| `androidx.constraintlayout:constraintlayout` | 2.2.1 | Xamarin.AndroidX.ConstraintLayout 2.2.1.6 |
| `androidx.constraintlayout:constraintlayout-core` | 1.1.1 | Xamarin.AndroidX.ConstraintLayout.Core 1.1.1.6 |
| `androidx.coordinatorlayout:coordinatorlayout` | 1.3.0 | Xamarin.AndroidX.CoordinatorLayout 1.3.0.6 |
| `androidx.core:core` | 1.19.0 | Xamarin.AndroidX.Core 1.19.0.1 |
| `androidx.core:core-ktx` | 1.19.0 | Xamarin.AndroidX.Core.Core.Ktx 1.19.0.1 |
| `androidx.core:core-viewtree` | 1.0.0 | Xamarin.AndroidX.Core.ViewTree 1.0.0.6 |
| `androidx.cursoradapter:cursoradapter` | 1.0.0 | Xamarin.AndroidX.CursorAdapter 1.0.0.37 |
| `androidx.customview:customview` | 1.2.0 | Xamarin.AndroidX.CustomView 1.2.0.4 |
| `androidx.customview:customview-poolingcontainer` | 1.1.0 | Xamarin.AndroidX.CustomView.PoolingContainer 1.1.0.4 |
| `androidx.drawerlayout:drawerlayout` | 1.2.0 | Xamarin.AndroidX.DrawerLayout 1.2.0.21 |
| `androidx.dynamicanimation:dynamicanimation` | 1.1.0 | Xamarin.AndroidX.DynamicAnimation 1.1.0.6 |
| `androidx.emoji2:emoji2` | 1.6.0 | Xamarin.AndroidX.Emoji2 1.6.0.3 |
| `androidx.emoji2:emoji2-views-helper` | 1.6.0 | Xamarin.AndroidX.Emoji2.ViewsHelper 1.6.0.3 |
| `androidx.fragment:fragment` | 1.8.9 | Xamarin.AndroidX.Fragment 1.8.9.3 |
| `androidx.graphics:graphics-shapes` | 1.1.0 | Xamarin.AndroidX.Graphics.Shapes 1.1.0.3 |
| `androidx.graphics:graphics-shapes-android` | 1.1.0 | Xamarin.AndroidX.Graphics.Shapes.Android 1.1.0.3 |
| `androidx.interpolator:interpolator` | 1.0.0 | Xamarin.AndroidX.Interpolator 1.0.0.37 |
| `androidx.lifecycle:lifecycle-common` | 2.11.0 | Xamarin.AndroidX.Lifecycle.Common 2.11.0.1 |
| `androidx.lifecycle:lifecycle-common-jvm` | 2.11.0 | Xamarin.AndroidX.Lifecycle.Common.Jvm 2.11.0.1 |
| `androidx.lifecycle:lifecycle-livedata-core` | 2.11.0 | Xamarin.AndroidX.Lifecycle.LiveData.Core 2.11.0.1 |
| `androidx.lifecycle:lifecycle-process` | 2.11.0 | Xamarin.AndroidX.Lifecycle.Process 2.11.0.1 |
| `androidx.lifecycle:lifecycle-runtime` | 2.11.0 | Xamarin.AndroidX.Lifecycle.Runtime 2.11.0.1 |
| `androidx.lifecycle:lifecycle-runtime-android` | 2.11.0 | Xamarin.AndroidX.Lifecycle.Runtime.Android 2.11.0.1 |
| `androidx.lifecycle:lifecycle-viewmodel` | 2.11.0 | Xamarin.AndroidX.Lifecycle.ViewModel 2.11.0.1 |
| `androidx.lifecycle:lifecycle-viewmodel-android` | 2.11.0 | Xamarin.AndroidX.Lifecycle.ViewModel.Android 2.11.0.1 |
| `androidx.lifecycle:lifecycle-viewmodel-savedstate` | 2.11.0 | Xamarin.AndroidX.Lifecycle.ViewModelSavedState 2.11.0.1 |
| `androidx.lifecycle:lifecycle-viewmodel-savedstate-android` | 2.11.0 | Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android 2.11.0.1 |
| `androidx.loader:loader` | 1.1.0 | Xamarin.AndroidX.Loader 1.1.0.37 |
| `androidx.navigationevent:navigationevent` | 1.1.2 | Xamarin.AndroidX.NavigationEvent 1.1.2.1 |
| `androidx.navigationevent:navigationevent-android` | 1.1.2 | Xamarin.AndroidX.NavigationEvent.Android 1.1.2.1 |
| `androidx.profileinstaller:profileinstaller` | 1.4.1 | Xamarin.AndroidX.ProfileInstaller.ProfileInstaller 1.4.1.8 |
| `androidx.recyclerview:recyclerview` | 1.4.0 | Xamarin.AndroidX.RecyclerView 1.4.0.6 |
| `androidx.resourceinspection:resourceinspection-annotation` | 1.0.1 | Xamarin.AndroidX.ResourceInspection.Annotation 1.0.1.25 |
| `androidx.savedstate:savedstate` | 1.5.0 | Xamarin.AndroidX.SavedState 1.5.0.1 |
| `androidx.savedstate:savedstate-android` | 1.5.0 | Xamarin.AndroidX.SavedState.SavedState.Android 1.5.0.1 |
| `androidx.startup:startup-runtime` | 1.2.0 | Xamarin.AndroidX.Startup.StartupRuntime 1.2.0.8 |
| `androidx.tracing:tracing` | 1.3.0 | Xamarin.AndroidX.Tracing.Tracing 1.3.0.4 |
| `androidx.tracing:tracing-android` | 1.3.0 | Xamarin.AndroidX.Tracing.Tracing.Android 1.3.0.4 |
| `androidx.transition:transition` | 1.7.0 | Xamarin.AndroidX.Transition 1.7.0.2 |
| `androidx.vectordrawable:vectordrawable` | 1.2.0 | Xamarin.AndroidX.VectorDrawable 1.2.0.11 |
| `androidx.vectordrawable:vectordrawable-animated` | 1.2.0 | Xamarin.AndroidX.VectorDrawable.Animated 1.2.0.11 |
| `androidx.versionedparcelable:versionedparcelable` | 1.2.1 | Xamarin.AndroidX.VersionedParcelable 1.2.1.6 |
| `androidx.viewpager:viewpager` | 1.1.0 | Xamarin.AndroidX.ViewPager 1.1.0.7 |
| `androidx.viewpager2:viewpager2` | 1.1.0 | Xamarin.AndroidX.ViewPager2 1.1.0.11 |

### A.10 Material Components for Android

- Artefakt: `com.google.android.material:material` 1.14.0 (NuGet Xamarin.Google.Android.Material 1.14.0.6)
- Plattform: Android
- Projekt: https://github.com/material-components/material-components-android
- Lizenz: Apache-2.0 (Text: [D.2](#d2-apache-license-20))
- Copyright (C) The Android Open Source Project (Material Components for Android, Google LLC)

### A.11 Kotlin und kotlinx

Plattform: Android. Lizenz jeweils Apache-2.0 (Text: [D.2](#d2-apache-license-20)), NOTICE-Dateien: [D.3](#d3-notice-dateien).

- **Kotlin-Standardbibliothek** – `org.jetbrains.kotlin:kotlin-stdlib` 2.4.0 (NuGet Xamarin.Kotlin.StdLib 2.4.0.1) –
  https://github.com/JetBrains/kotlin – Copyright 2010-2024 JetBrains s.r.o. and Kotlin Programming Language
  contributors. Laut `license/README.md` des Kotlin-Projekts enthält die Standardbibliothek Code Dritter:
  - Teile von `kotlin.collections`: abgeleitet von GWT, (C) 2007-08 Google Inc. – Apache-2.0
  - `kotlin.time`: Copyright (c) 2007-present, Stephen Colebourne & Michael Nascimento Santos (ThreeTen-BP) –
    BSD-3-Clause (vollständiger Text: [D.5](#d5-bsd-3-clause--threeten-bp-in-der-kotlin-standardbibliothek))
  - `UnsignedJVM.kt`: abgeleitet von Guava `UnsignedLongs`, (C) 2011 The Guava Authors – Apache-2.0
  - `MathJVM.kt`: abgeleitet von den Boost Special Math Functions, Copyright Eric Ford & Hubert Holin 2001 –
    Boost Software License 1.0 (Text: [D.6](#d6-boost-software-license-10))
- **kotlinx.coroutines** – `org.jetbrains.kotlinx:kotlinx-coroutines-core`, `-core-jvm`, `-android` 1.11.0 –
  https://github.com/Kotlin/kotlinx.coroutines – Copyright 2016-2025 JetBrains s.r.o and contributors
- **kotlinx.serialization** – `org.jetbrains.kotlinx:kotlinx-serialization-core`, `-core-jvm` 1.11.0 –
  https://github.com/Kotlin/kotlinx.serialization – Copyright 2017-2019 JetBrains s.r.o and respective authors and
  developers
- **kotlinx-atomicfu** – `org.jetbrains.kotlinx:atomicfu`, `atomicfu-jvm` 0.33.0 –
  https://github.com/Kotlin/kotlinx-atomicfu – Copyright 2017 JetBrains s.r.o. and respective authors and developers

### A.12 Annotations-Bibliotheken

Plattform: Android. Lizenz jeweils Apache-2.0 (Text: [D.2](#d2-apache-license-20)).

- **Guava ListenableFuture** – `com.google.guava:listenablefuture` 1.0 (NuGet Xamarin.Google.Guava.ListenableFuture
  1.0.0.32) – https://github.com/google/guava – Copyright (C) 2007 The Guava Authors
- **Error Prone Annotations** – `com.google.errorprone:error_prone_annotations` 2.50.0 (NuGet
  Xamarin.Google.ErrorProne.Annotations 2.50.0.1) – https://github.com/google/error-prone – Copyright The Error Prone
  Authors (Google Inc., Two Sigma Open Source, Picnic Technologies)
- **JetBrains Java Annotations** – `org.jetbrains:annotations` 26.1.0 (NuGet Xamarin.Jetbrains.Annotations 26.1.0.2) –
  https://github.com/JetBrains/java-annotations – Copyright 2000-2016 JetBrains s.r.o.
- **JSpecify** – `org.jspecify:jspecify` 1.0.0 (NuGet Xamarin.JSpecify 1.0.0.7) – https://github.com/jspecify/jspecify –
  Copyright The JSpecify Authors (Google LLC, Oracle America, Inc., SpotBugs Team)

### A.13 Shizuku API

- Artefakte: `dev.rikka.shizuku:api`, `:provider`, `:shared`, `:aidl` 13.1.5 (Maven Central; ins APK paketiert,
  Aufruf per Reflection). Die Shizuku-App selbst wird nicht mitgeliefert.
- Plattform: Android
- Projekt: https://github.com/RikkaApps/Shizuku-API
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright (c) 2021 RikkaW

### A.14 Material Icons

- Verwendung: Vektor-Symbole in `android/NConnect.Android/Resources/drawable/` – `check` (ic_check), `close`
  (ic_cross), `warning` (ic_warn), `bluetooth` (ic_bluetooth), `battery_full` (ic_battery), `videogame_asset`
  (ic_nav_controller, ic_stat_controller), `help_outline` (ic_nav_help), `tune` (ic_nav_settings)
- Plattform: Android
- Projekt: https://github.com/google/material-design-icons
- Lizenz: Apache-2.0 (Text: [D.2](#d2-apache-license-20))
- Copyright Google LLC

### A.15 Linux.Bluetooth

- Version: 6.0.0-pre3 (NuGet)
- Plattform: Linux/Kodi (Dienst `nconnect`)
- Projekt: https://github.com/SuessLabs/Linux.Bluetooth
- Lizenz: Apache-2.0 (Text: [D.2](#d2-apache-license-20)); keine NOTICE-Datei
- Copyright 2022 Xeno Innovations, Inc. (laut LICENSE); Copyright 2026 Xeno Innovations, Inc. (Angabe im Paket);
  Autoren: Xeno Innovations, Suess Labs

### A.16 Tmds.DBus

- Version: 0.95.1 (NuGet)
- Plattform: Linux/Kodi (Dienst `nconnect`)
- Projekt: https://github.com/tmds/Tmds.DBus
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright 2006 Alp Toker <alp@atoker.com>
- Copyright 2010 Other Contributors
- Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>

### A.17 .NET-Laufzeit (Linux)

- Version: .NET 10 (Microsoft.NETCore.App 10.0.x für linux-x64, linux-arm64, linux-arm), self-contained und gekürzt
  in der Programmdatei `nconnect` enthalten
- Plattform: Linux/Kodi
- Projekt: https://github.com/dotnet/runtime
- Lizenz: MIT (Text: [D.1](#d1-mit-lizenz))
- Copyright (c) .NET Foundation and Contributors
- Hinweise zu enthaltenen Komponenten Dritter: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

---

## B – Übernommener oder angepasster Quellcode und Daten

### B.1 SDL – Simple DirectMedia Layer

- Quelle: SDL 3, Quelltextstand 2026 – `src/joystick/hidapi/SDL_hidapi_switch2.c`, `SDL_hidapi_switch.c`,
  `SDL_hidapi_ps4.c`, `SDL_hidapi_ps5.c`
- Plattform: alle
- Projekt: https://www.libsdl.org – https://github.com/libsdl-org/SDL
- Lizenz: zlib (vollständiger Text: [D.7](#d7-zlib-lizenz--sdl))
- Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>
- In den Switch-, Switch-2- und PS4-Treibern von SDL: „Code and logic contributed by Valve Corporation under the SDL
  zlib license.“

Nach C# übertragen und angepasst (geänderte Fassung, nicht die Original-Software):

- `Switch2Pro.Protocol/Commands.cs` – USB-Start-Sequenz (`HIDAPI_DriverSwitch2_InitUSB`) und Speicherlesen über USB
- `Switch2Pro.Protocol/Rumble.cs` – Aufbau des HD-Rumble-2-Frames und Amplitudengrenze (`UpdateRumble`)
- `Switch2Pro.Protocol/Calibration.cs` – Format der Stick-Kalibrierung (`ParseStickCalibration`)
- `Switch2Pro.Protocol/Switch1.cs` – Switch-1-HD-Rumble (Amplitudentabellen und Frequenzcodes aus
  `SDL_hidapi_switch.c`; die Tabellen gehen auf dekuNukem/Nintendo_Switch_Reverse_Engineering zurück) und
  Achsenzuordnung der Bewegungssensoren
- `Switch2Pro.Protocol/Mapping.cs` – Achsenzuordnung des Gyros
- `Switch2Pro.Protocol/PlayStationPad.cs` – Berichtsformate DualShock 4/DualSense
- `Switch2Pro.Protocol/WiredSwitchPad.cs` – Berichtsformat lizenzierter Kabel-Gamepads
- Windows: `Links/Switch2UsbLink.cs`, `Links/Switch1HidLink.cs` – Abläufe und Zeitabstände

### B.2 NS2Pro-Bridge-Windows, Switch2BTLink, joycon2cpp

- **NS2Pro-Bridge-Windows** – https://github.com/TohruUehera/NS2Pro-Bridge-Windows – MIT
  (Text: [D.1](#d1-mit-lizenz)) – Copyright (c) 2026 NS2 Pro Compatibility contributors
- Plattform: alle
- In SKZ Connect: Bluetooth-Start-Sequenz (`Switch2Pro.Protocol/Commands.cs`, `InitSequence`) und das
  Bluetooth-Vibrationspaket (`Switch2Pro.Protocol/Rumble.cs`).

Laut THIRD_PARTY_NOTICES.md von NS2Pro-Bridge-Windows stammen diese Protokollkonstanten aus:

- **Switch2BTLink** – https://github.com/KumuIi/Switch2BTLink – MIT (Text: [D.1](#d1-mit-lizenz)) –
  Copyright (c) 2026 KumuIi. Vermerk des Projekts: „Based on protocol research from the joycon2py and joycon2cpp
  projects (MIT License, Copyright (c) TheFrano and contributors) and BLE reverse engineering by german77.“
- **joycon2cpp** – https://github.com/TheFrano/joycon2cpp – MIT (Text: [D.1](#d1-mit-lizenz)) –
  Copyright (c) 2025 Frano

### B.3 Android Compatibility Test Suite (CTS) – Xbox-One-S-Deskriptor

- Quelle: `tests/tests/hardware/res/raw/microsoft_xboxones_register.json` (und zugehörige Testberichte)
- Plattform: alle (Bestandteil von `Switch2Pro.Protocol`; genutzt für den virtuellen Controller unter Android)
- Projekt: https://android.googlesource.com/platform/cts/
- Lizenz: Apache-2.0 (Text: [D.2](#d2-apache-license-20))
- Copyright (C) The Android Open Source Project
- In SKZ Connect: HID-Berichtsdeskriptor und Berichtsaufbau des Xbox One S Controllers (Bluetooth) in
  `Switch2Pro.Protocol/XboxBtReport.cs`

---

## C – Protokoll-Dokumentation (kein übernommener Code)

Die folgenden Quellen wurden nur als Beschreibung von Protokollen, Gerätekennungen und Messwerten genutzt; es wurde
kein Quellcode daraus übernommen. Die Namen werden zur Würdigung genannt.

- **ndeadly/switch2_controller_research** – Reverse-Engineering-Dokumentation des Switch-2-Controller-Protokolls
  (BLE-GATT, Eingabeberichte, Kopplung): https://github.com/ndeadly/switch2_controller_research
- **dekuNukem/Nintendo_Switch_Reverse_Engineering** – Switch-1-Unterbefehle und Rumble-Datentabelle:
  https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering
- **Linux-Kernel** – Treiber `hid-nintendo` (Gerätetypen und Tastenbits der Nintendo-Switch-Online-Controller),
  `hid-wiimote` und `hid-playstation` (Berichtsformate): https://git.kernel.org
- **WiiBrew** – Wii-Fernbedienung, Erweiterungen, Wii Motion Plus, IR-Kamera, Wii U Pro Controller:
  https://wiibrew.org
- **Dolphin** – Kopplung von Wii-Fernbedienungen mit binärer PIN: https://dolphin-emu.org
- **yuzu/Citron** – Ablauf und Konstanten für NFC (amiibo), IR-Kamera und Ring-Con
- **Switch2Connect** – Funktionsideen und Schwellwerte (Akkuspannung, Erkennung „auf einer Oberfläche“)
- **Cemuhook/DSU-Protokoll** – offene Protokollbeschreibung für den DSU-Server; **DS4Windows** – Reihenfolge der
  Lichtleisten-Bytes
- **AOSP `frameworks/base/cmds/hid`** – JSON-Befehlsformat des Android-Shell-Befehls `hid`

---

## D – Lizenztexte

### D.1 MIT-Lizenz

Der folgende Lizenztext gilt für diese Komponenten mit ihren Copyright-Vermerken:

| Komponente | Copyright-Vermerk |
|---|---|
| Nefarius.ViGEm.Client (A.1) | Copyright (c) 2018 Benjamin Höglinger-Stelzer; Copyright © Nefarius Software Solutions e.U. 2017-2023 |
| ViGEmClient (A.1) | Copyright (c) 2018 Benjamin Höglinger-Stelzer |
| Costura (A.1) | Copyright (c) 2012 Simon Cropp and contributors |
| HidHide (A.3) | Copyright (c) 2020 Eric Korff de Gidts; Copyright (c) 2021-2023 Benjamin Höglinger-Stelzer |
| .NET-Laufzeit, Windows Forms, .NET für Android (A.4, A.7, A.17) | Copyright (c) .NET Foundation and Contributors. All rights reserved. |
| .NET-for-Android-Bindungen (A.8) | Copyright (c) .NET Foundation Contributors |
| C#/WinRT (A.5) | Copyright (c) Microsoft Corporation. |
| Shizuku API (A.13) | Copyright (c) 2021 RikkaW |
| Tmds.DBus (A.16) | Copyright 2006 Alp Toker <alp@atoker.com>; Copyright 2010 Other Contributors; Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com> |
| NS2Pro-Bridge-Windows (B.2) | Copyright (c) 2026 NS2 Pro Compatibility contributors |
| Switch2BTLink (B.2) | Copyright (c) 2026 KumuIi |
| joycon2cpp (B.2) | Copyright (c) 2025 Frano |

```text
MIT License

Copyright (c) <siehe Tabelle oben / see table above>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### D.2 Apache License 2.0

Gilt für: AndroidX (A.9), Material Components for Android (A.10), Kotlin-Standardbibliothek, kotlinx.coroutines,
kotlinx.serialization, kotlinx-atomicfu (A.11), Guava ListenableFuture, Error Prone Annotations, JetBrains Java
Annotations, JSpecify (A.12), Material Icons (A.14), Linux.Bluetooth (A.15) und Android CTS (B.3). Die
Copyright-Vermerke stehen in den jeweiligen Abschnitten.

```text
                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS
```

### D.3 NOTICE-Dateien

Nach Abschnitt 4 (d) der Apache License 2.0 werden die NOTICE-Dateien der Komponenten unverändert wiedergegeben.

Kotlin (https://github.com/JetBrains/kotlin, `license/NOTICE.txt`):

```text
   =========================================================================
   ==  NOTICE file corresponding to the section 4 d of                    ==
   ==  the Apache License, Version 2.0,                                   ==
   ==  in this case for the Kotlin Compiler distribution.                 ==
   =========================================================================

   Kotlin Compiler
   Copyright 2010-2024 JetBrains s.r.o and respective authors and developers
```

kotlinx.coroutines (`license/NOTICE.txt`):

```text
=========================================================================
==  NOTICE file corresponding to the section 4 d of                    ==
==  the Apache License, Version 2.0,                                   ==
==  in this case for the kotlinx.coroutines library.                   ==
=========================================================================

kotlinx.coroutines library.
Copyright 2016-2025 JetBrains s.r.o and contributors
```

kotlinx.serialization (`license/NOTICE.txt`):

```text
   =========================================================================
   ==  NOTICE file corresponding to the section 4 d of                    ==
   ==  the Apache License, Version 2.0,                                   ==
   ==  in this case for the kotlix.serialization library.                 ==
   =========================================================================

   kotlinx.serialization library.
   Copyright 2017-2019 JetBrains s.r.o and respective authors and developers
```

kotlinx-atomicfu (`license/NOTICE.txt`):

```text
   =========================================================================
   ==  NOTICE file corresponding to the section 4 d of                    ==
   ==  the Apache License, Version 2.0,                                   ==
   ==  in this case for the kotlix.atomicfu library.                      ==
   =========================================================================

   kotlinx.atoimcfu library.
   Copyright 2017 JetBrains s.r.o and respective authors and developers
```

### D.4 BSD-3-Clause – ViGEmBus

```text
BSD 3-Clause License

Copyright (c) 2016-2020, Nefarius Software Solutions e.U.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### D.5 BSD-3-Clause – ThreeTen-BP (in der Kotlin-Standardbibliothek)

```text
Copyright (c) 2007-present, Stephen Colebourne & Michael Nascimento Santos.

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice,
  this list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of JSR-310 nor the names of its contributors
  may be used to endorse or promote products derived from this software
  without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### D.6 Boost Software License 1.0

Gilt für den aus Boost abgeleiteten Teil der Kotlin-Standardbibliothek (A.11); Copyright Eric Ford & Hubert Holin 2001.

```text
Boost Software License - Version 1.0 - August 17th, 2003

Permission is hereby granted, free of charge, to any person or organization
obtaining a copy of the software and accompanying documentation covered by
this license (the "Software") to use, reproduce, display, distribute,
execute, and transmit the Software, and to prepare derivative works of the
Software, and to permit third-parties to whom the Software is furnished to
do so, all subject to the following:

The copyright notices in the Software and this entire statement, including
the above license grant, this restriction and the following disclaimer,
must be included in all copies of the Software, in whole or in part, and
all derivative works of the Software, unless such copies or derivative
works are solely in the form of machine-executable object code generated by
a source language processor.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

### D.7 zlib-Lizenz – SDL

```text
Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

### D.8 Inno Setup License

```text
Inno Setup License
==================

Except where otherwise noted, all of the documentation and software included in the Inno
Setup package is copyrighted by Jordan Russell.

Copyright (C) 1997-2026 Jordan Russell. All rights reserved.
Portions Copyright (C) 2000-2026 Martijn Laan. All rights reserved.

This software is provided "as-is," without any express or implied warranty. In no event shall
the author be held liable for any damages arising from the use of this software.

Permission is granted to anyone to use this software for any purpose, including commercial
applications, and to alter and redistribute it, provided that the following conditions are met:

1. All redistributions of source code files must retain all copyright notices that are currently
   in place, and this list of conditions without modification.

2. All redistributions in binary form must retain all occurrences of the above copyright notice
   and web site addresses that are currently in place (for example, in the About boxes).

3. The origin of this software must not be misrepresented; you must not claim that you wrote
   the original software. If you use this software to distribute a product, an acknowledgment
   in the product documentation would be appreciated but is not required.

4. Modified versions in source or binary form must be plainly marked as such, and must not
   be misrepresented as being the original software.


Jordan Russell
jr-2020 AT jrsoftware.org
https://jrsoftware.org/
```
